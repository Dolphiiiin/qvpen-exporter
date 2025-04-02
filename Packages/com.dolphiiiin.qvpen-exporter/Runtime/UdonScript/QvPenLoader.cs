using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Data;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using System.Collections;

namespace QvPenExporter
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QvPenLoader : UdonSharpBehaviour
    {
        [Header("Drawing Settings")]
        [SerializeField]
        private Transform inkParent;

        [SerializeField]
        private GameObject lineRendererPrefab;

        [SerializeField]
        private Material defaultMaterial;

        [Header("Pickup Settings")]
        [SerializeField, Tooltip("ペンをピックアップ可能なオブジェクトとして生成するか")]
        private Toggle pickupToggle;

        [SerializeField, Tooltip("あらかじめシーンに配置されたピックアップオブジェクト（非アクティブ状態で配置してください）")]
        private GameObject fixedPickupObject;

        [SerializeField, Tooltip("ピックアップオブジェクト内の線を格納する親オブジェクト")]
        private Transform pickupLineParent;
        
        [SerializeField, Tooltip("ピックアップオブジェクトのリセット位置")]
        private Transform pickupResetPosition;
        
        [UdonSynced, Tooltip("ピックアップオブジェクトのアクティブ状態")]
        private bool isPickupActive = false;

        [Header("Default Parameters")]
        [SerializeField, Tooltip("JSONでwidthが定義されていない場合のデフォルトの太さ")]
        private float defaultWidth = 0.005f;
    
        [SerializeField] 
        private int lineLayer = 0;

        [Header("UI Components")]
        [SerializeField]
        private VRCUrlInputField urlInputField;
    
        [SerializeField]
        private Text statusText;

        [SerializeField, Tooltip("スケール値を入力するInputField（0.1なら0.1倍にスケーリング）")]
        private InputField scaleInputField;

        private float loadStartTime;
        private bool isLoading = false;
        private const float LOAD_TIMEOUT_SECONDS = 30.0f;

        private bool isInitialized = false;

        [UdonSynced]
        private VRCUrl syncedUrl;

        [UdonSynced]
        private bool isImporting = false; // インポート中フラグ

        [UdonSynced]
        private bool hasImportedInWorld = false; // ワールドでインポートされたことがあるかどうか

        private bool initialLoadExecuted = false; // 初期化時のロードが実行済みかどうか

        private VRCUrl lastLoadedUrl;

        // Pickupオブジェクトの初期位置と回転を保存する変数
        private Vector3 pickupInitialPosition;
        private Quaternion pickupInitialRotation;
        private bool hasInitialTransform = false;

        private float currentScale = 1.0f; // デフォルトのスケール値は1.0

        // サイズチェック関連の変数
        private const float MAX_BOUNDING_SIZE = 5.0f; // バウンディングサイズの最大許容値（メートル）
        private VRCUrl _sizeCheckUrl;   // 現在サイズチェック中のURL
        private VRCUrl _approvedUrl;    // サイズが大きいが承認済みのURL
        private bool _manuallyImported = false; // 手動でインポートが実行されたかどうか

        private void Start()
        {
            if (lineRendererPrefab == null)
            {
                Debug.LogError("[QvPenLoader] LineRenderer prefab is not assigned");
                UpdateStatusUI("エラー: LineRenderer prefabが見つかりません", Color.red);
                return;
            }
        
            if (defaultMaterial == null)
            {
                Debug.LogWarning("[QvPenLoader] Default material is not assigned. Lines may not render correctly.");
            }
        
            if (inkParent == null)
            {
                inkParent = transform;
            }

            if (urlInputField == null)
            {
                Debug.LogError("[QvPenLoader] URL InputField not set");
                UpdateStatusUI("エラー: URL入力フィールドが見つかりません", Color.red);
                return;
            }
            
            // ピックアップオブジェクトの検証
            if (fixedPickupObject == null)
            {
                Debug.LogWarning("[QvPenLoader] Fixed pickup object is not assigned. Pickup mode will be disabled.");
                if (pickupToggle != null)
                {
                    pickupToggle.isOn = false;
                    pickupToggle.interactable = false;
                }
            }
            else if (pickupLineParent == null)
            {
                // ピックアップオブジェクト内に線を格納する親がない場合は、ピックアップオブジェクト自体を親にする
                pickupLineParent = fixedPickupObject.transform;
                Debug.LogWarning("[QvPenLoader] Pickup line parent is not assigned. Using fixed pickup object as parent.");
            }
            
            // リセット位置の検証
            if (pickupResetPosition == null && fixedPickupObject != null)
            {
                Debug.LogWarning("[QvPenLoader] Pickup reset position is not assigned. Using pickup object's initial position.");
                // リセット位置が指定されていない場合は、ピックアップオブジェクトの初期位置を記録
                pickupInitialPosition = fixedPickupObject.transform.position;
                pickupInitialRotation = fixedPickupObject.transform.rotation;
                hasInitialTransform = true;
            }
            
            // 初期状態ではピックアップオブジェクトを非アクティブにする
            UpdatePickupObjectState(false);

            // 初期状態の設定
            isInitialized = true;
            UpdateStatusUI("準備完了", Color.white);
            Debug.Log("[QvPenLoader] Initialization completed successfully");

            // 既にワールドでインポートされている場合は初期ロードを実行
            if (hasImportedInWorld && !initialLoadExecuted && syncedUrl != null)
            {
                initialLoadExecuted = true;
                StartImport(syncedUrl);
            }

            // スケール入力フィールドの初期化
            if (scaleInputField != null)
            {
                scaleInputField.text = "1.0"; // デフォルトのスケール値を明示的に設定
                UpdateScaleFromInput();
            }
        }

        // ピックアップオブジェクトのアクティブ状態を更新する
        private void UpdatePickupObjectState(bool active)
        {
            if (fixedPickupObject != null)
            {
                isPickupActive = active;
                
                if (active)
                {
                    // アクティブ化する際に位置と回転をリセット
                    if (pickupResetPosition != null)
                    {
                        // リセット位置が設定されている場合はそれを使用
                        fixedPickupObject.transform.position = pickupResetPosition.position;
                        fixedPickupObject.transform.rotation = pickupResetPosition.rotation;
                    }
                    else if (hasInitialTransform)
                    {
                        // リセット位置がない場合は保存された初期位置を使用
                        fixedPickupObject.transform.position = pickupInitialPosition;
                        fixedPickupObject.transform.rotation = pickupInitialRotation;
                    }
                }
                
                fixedPickupObject.SetActive(active);
                
                // 非アクティブ化する場合はピックアップ内のLineRendererを持つオブジェクトのみをクリア
                if (!active && pickupLineParent != null)
                {
                    // 子オブジェクトを配列にコピー (foreachループ中にDestroyするとエラーになるため)
                    Transform[] children = new Transform[pickupLineParent.childCount];
                    for (int i = 0; i < pickupLineParent.childCount; i++)
                    {
                        children[i] = pickupLineParent.GetChild(i);
                    }
                    
                    foreach (Transform child in children)
                    {
                        // LineRendererコンポーネントを持つオブジェクトのみ削除
                        if (child.GetComponent<LineRenderer>() != null)
                        {
                            Destroy(child.gameObject);
                            Debug.Log($"[QvPenLoader] Destroyed line object: {child.name}");
                        }
                    }
                }
            }
        }

        private void UpdateStatusUI(string message, Color color)
        {
            if (statusText != null)
            {
                statusText.text = message;
                statusText.color = color;
            }
        }

        public void OnImportButtonClicked()
        {
            if (!isInitialized)
            {
                UpdateStatusUI("エラー: 初期化されていません", Color.red);
                return;
            }

            if (urlInputField == null)
            {
                UpdateStatusUI("エラー: URL入力フィールドが見つかりません", Color.red);
                return;
            }

            VRCUrl importUrl = urlInputField.GetUrl();
            if (importUrl == null || string.IsNullOrEmpty(importUrl.Get()))
            {
                UpdateStatusUI("エラー: URLを入力してください", Color.red);
                return;
            }

            // オーナー権限を取得
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // 手動インポートフラグを設定
            _manuallyImported = true;
            
            // URLを同期用変数に設定
            syncedUrl = importUrl;
            lastLoadedUrl = importUrl;
            hasImportedInWorld = true;
            RequestSerialization();

            // 全プレイヤーに対してインポート開始を通知
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkImport));
        }

        public void NetworkImport()
        {
            if (syncedUrl == null || string.IsNullOrEmpty(syncedUrl.Get()))
            {
                Debug.LogError("[QvPenLoader] Synced URL is empty");
                return;
            }

            StartImport(syncedUrl);
        }

        private void StartImport(VRCUrl url)
        {
            if (!isInitialized || url == null || string.IsNullOrEmpty(url.Get()))
            {
                UpdateStatusUI("エラー: 無効なURL", Color.red);
                return;
            }

            if (isLoading)
            {
                Debug.LogWarning("[QvPenLoader] Already loading another URL");
                UpdateStatusUI("別のURLを読み込み中です", Color.yellow);
                return;
            }

            // 他のプレイヤーがインポート中の場合は待機
            if (isImporting && !Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Debug.LogWarning("[QvPenLoader] Another player is importing");
                UpdateStatusUI("他のプレイヤーがインポート中です", Color.yellow);
                return;
            }

            // URLを記録
            Debug.Log($"[QvPenLoader] Starting import for URL: {url.Get()}");
            
            // 初期ロード（ワールド入室時）の場合はサイズチェックをスキップ
            // 手動インポートかどうかと初期化時のロード状態を詳細にログ出力
            Debug.Log($"[QvPenLoader] インポート状態: hasImportedInWorld={hasImportedInWorld}, initialLoadExecuted={initialLoadExecuted}, _manuallyImported={_manuallyImported}");
            
            // 初期ロード判定: ワールドに入った時の自動ロード（手動ではない場合のみ）
            bool isInitialJoinLoad = hasImportedInWorld && !initialLoadExecuted && !_manuallyImported;
            
            // 初期ロード実行時は、確実にフラグを立てる
            if (isInitialJoinLoad)
            {
                initialLoadExecuted = true;
                Debug.Log("[QvPenLoader] 初期ロード（ワールド入室時）として処理します。サイズチェックをスキップします。");
            }
            
            // 承認済みのURLかどうかチェック（同じURLで2回目の読み込み）
            bool isApproved = _approvedUrl != null && !string.IsNullOrEmpty(_approvedUrl.Get()) && 
                              url.Get().Equals(_approvedUrl.Get());
            
            // サイズチェック条件を詳細に出力
            Debug.Log($"[QvPenLoader] サイズチェック条件: isInitialJoinLoad={isInitialJoinLoad}, isApproved={isApproved}");
            
            // サイズチェックが必要かどうか判定
            bool skipSizeCheck = isInitialJoinLoad || isApproved;
            
            if (skipSizeCheck)
            {
                // サイズチェックをスキップしてインポート実行
                Debug.Log($"[QvPenLoader] サイズチェックをスキップ: 初期ロード={isInitialJoinLoad}, 承認済みURL={isApproved}");
                ImportWithoutSizeCheck(url);
            }
            else
            {
                // サイズチェック実行
                Debug.Log($"[QvPenLoader] サイズチェックを実行: {url.Get()}");
                _sizeCheckUrl = url;
                StartLoading();
                
                UpdateStatusUI("サイズチェック中...", Color.yellow);
                VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
            }
        }
        
        // サイズチェックなしでインポートする（初回読み込みか承認済みの場合）
        private void ImportWithoutSizeCheck(VRCUrl url)
        {
            // 既存のデータをクリア
            ClearExistingData();
            
            // ローディング状態の設定
            StartLoading();
            
            // オーナーがロード開始時は他のプレイヤーに通知
            if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                isImporting = true;
                RequestSerialization();
            }
            
            // 状態更新
            UpdateStatusUI("読み込み中...", Color.yellow);
            Debug.Log($"[QvPenLoader] Starting import without size check: {url.Get()}");
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
        }

        public void ReloadLastUrl()
        {
            if (lastLoadedUrl != null && !string.IsNullOrEmpty(lastLoadedUrl.Get()))
            {
                LoadFromUrl(lastLoadedUrl);
            }
            else
            {
                Debug.LogWarning("[QvPenLoader] No URL to reload");
                UpdateStatusUI("再ロードするURLがありません", Color.yellow);
            }
        }

        public void LoadFromUrl(VRCUrl url)
        {
            // オーナー権限を取得
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // URLを同期用変数に設定
            syncedUrl = url;
            lastLoadedUrl = url;
            hasImportedInWorld = true;
            RequestSerialization();

            // 全プレイヤーに対してインポート開始を通知
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkImport));
        }

        // データクリアの内部メソッド
        private void ClearExistingData()
        {
            foreach (Transform child in inkParent)
            {
                Destroy(child.gameObject);
            }
            
            // ピックアップオブジェクト内のLineRendererを持つオブジェクトのみをクリア
            if (pickupLineParent != null)
            {
                // 子オブジェクトを配列にコピー (foreachループ中にDestroyするとエラーになるため)
                Transform[] children = new Transform[pickupLineParent.childCount];
                for (int i = 0; i < pickupLineParent.childCount; i++)
                {
                    children[i] = pickupLineParent.GetChild(i);
                }
                
                foreach (Transform child in children)
                {
                    // LineRendererコンポーネントを持つオブジェクトのみ削除
                    if (child.GetComponent<LineRenderer>() != null)
                    {
                        Destroy(child.gameObject);
                        Debug.Log($"[QvPenLoader] Cleared line object from pickup: {child.name}");
                    }
                }
            }
            
            // ピックアップオブジェクトを非アクティブ化（オーナーであれば同期する）
            if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                UpdatePickupObjectState(false);
                RequestSerialization();
            }
            
            Debug.Log("[QvPenLoader] Cleared existing data before import");
        }

        private void StartLoading()
        {
            loadStartTime = Time.time;
            isLoading = true;
        }

        private void StopLoading()
        {
            isLoading = false;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            float loadTime = Time.time - loadStartTime;
            Debug.Log($"[QvPenLoader] StringLoad success after {loadTime:F2} seconds");

            string jsonData = result.Result;
            if (string.IsNullOrEmpty(jsonData))
            {
                Debug.LogError("[QvPenLoader] Received empty data");
                UpdateStatusUI("エラー: データが空です", Color.red);
                
                // エラー時はインポート状態をリセット
                isImporting = false;
                if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
                {
                    RequestSerialization();
                }
                StopLoading();
                return;
            }

            // サイズチェック中の場合
            if (_sizeCheckUrl != null && !string.IsNullOrEmpty(_sizeCheckUrl.Get()))
            {
                ProcessSizeCheck(jsonData);
                return;
            }

            // 通常のインポート処理
            ProcessNormalImport(jsonData);
        }

        // サイズチェック処理を行う
        private void ProcessSizeCheck(string jsonData)
        {
            VRCUrl urlToCheck = _sizeCheckUrl;
            string urlString = urlToCheck.Get();
            _sizeCheckUrl = null; // チェック状態をクリア
            
            Debug.Log($"[QvPenLoader] Size checking in progress for URL: {urlString}");
            
            // スケール値を確実に適用するため、チェック前に再度確認
            if (scaleInputField != null)
            {
                Debug.Log($"[QvPenLoader] Scale input field text: {scaleInputField.text}");
                UpdateScaleFromInput();
            }
            else
            {
                Debug.LogWarning("[QvPenLoader] scaleInputField is null");
            }
            
            Debug.Log($"[QvPenLoader] Current scale value for size check: {currentScale}");
            
            // バウンディングサイズを計算
            float boundingSize = CalculateBoundingSizeFromJson(jsonData);
            Debug.Log($"[QvPenLoader] Size check result: {boundingSize:F2}m (Max allowed: {MAX_BOUNDING_SIZE:F2}m)");
            
            // サイズが大きい場合は警告を表示
            if (boundingSize > MAX_BOUNDING_SIZE)
            {
                Debug.LogWarning($"[QvPenLoader] Large model detected: {boundingSize:F1}m bounding size exceeds limit of {MAX_BOUNDING_SIZE:F1}m");
                UpdateStatusUI($"警告: {boundingSize:F1}mの巨大なモデルを読み込もうとしています。承認するには再度読み込みます", Color.yellow);
                
                // 次回同じURLがロードされたらスキップするために保存
                _approvedUrl = urlToCheck;
                
                Debug.Log($"[QvPenLoader] URL marked for confirmation: {urlString}");
                
                StopLoading();
                return;
            }
            
            // サイズが許容範囲内なら通常のインポートを実行
            Debug.Log($"[QvPenLoader] Size check passed. Size {boundingSize:F2}m is within limit, proceeding with import");
            
            // サイズチェックの状態をリセット
            StopLoading();
            
            // ClearExistingDataを実行してからインポートするように修正
            ClearExistingData();
            
            // 通常の読み込み処理を開始
            ImportWithoutSizeCheck(urlToCheck);
        }

        // 通常のインポート処理を行う
        private void ProcessNormalImport(string jsonData)
        {
            Debug.Log("[QvPenLoader] Processing normal import data");
            
            // スケール値を確実に適用するため、読み込み前に再度確認
            if (scaleInputField != null)
            {
                UpdateScaleFromInput();
            }
            
            // エラー処理を含むデータのインポート
            bool importSuccess = ProcessImportData(jsonData);
            if (importSuccess)
            {
                UpdateStatusUI("インポート完了！", Color.green);
            }
            else
            {
                Debug.LogError("[QvPenLoader] Error occurred during import data processing");
                UpdateStatusUI("エラー: データ処理中にエラーが発生しました", Color.red);
            }

            // 同期フラグを更新
            isImporting = false;
            if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                RequestSerialization();
            }
            
            StopLoading();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            float loadTime = Time.time - loadStartTime;
            Debug.LogError($"[QvPenLoader] StringLoad error after {loadTime:F2} seconds: {result.ErrorCode} - {result.Error}");
            
            UpdateStatusUI("エラー: 読み込みに失敗しました", Color.red);

            // エラー時は他のプレイヤーがインポートできるようにフラグを解除
            isImporting = false;
            if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                RequestSerialization();
            }
            StopLoading();
        }

        public override void OnDeserialization()
        {
            // ピックアップオブジェクトの状態を同期
            if (fixedPickupObject != null)
            {
                // アクティブ状態を適用
                if (fixedPickupObject.activeSelf != isPickupActive)
                {
                    // アクティブ化する場合は位置と回転をリセット
                    if (isPickupActive)
                    {
                        if (pickupResetPosition != null)
                        {
                            fixedPickupObject.transform.position = pickupResetPosition.position;
                            fixedPickupObject.transform.rotation = pickupResetPosition.rotation;
                        }
                        else if (hasInitialTransform)
                        {
                            fixedPickupObject.transform.position = pickupInitialPosition;
                            fixedPickupObject.transform.rotation = pickupInitialRotation;
                        }
                    }
                    
                    fixedPickupObject.SetActive(isPickupActive);
                }
            }
            
            // 初期化済みで、かつワールドでインポートされたことがあり、まだ初期ロードを実行していない場合のみロード
            if (isInitialized && hasImportedInWorld && !initialLoadExecuted && syncedUrl != null)
            {
                initialLoadExecuted = true;
                StartImport(syncedUrl);
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!player.isLocal) return;

            // 初期化済みで、かつワールドでインポートされたことがあり、まだ初期ロードを実行していない場合のみロード
            if (isInitialized && hasImportedInWorld && !initialLoadExecuted && syncedUrl != null)
            {
                initialLoadExecuted = true;
                StartImport(syncedUrl);
            }
        }

        private bool ProcessImportData(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                Debug.LogError("[QvPenLoader] Loaded data is empty");
                return false;
            }

            DataToken dataToken;
            bool success = VRCJson.TryDeserializeFromJson(data, out dataToken);
            if (!success || dataToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[QvPenLoader] Failed to parse JSON data");
                return false;
            }
        
            DataDictionary jsonData = dataToken.DataDictionary;
        
            if (!jsonData.TryGetValue("exportedData", TokenType.DataList, out DataToken exportedDataToken))
            {
                Debug.LogError("JSON data does not contain exportedData array");
                return false;
            }
        
            DataList exportedData = exportedDataToken.DataList;
            int exportedCount = exportedData.Count;
        
            if (exportedCount == 0)
            {
                Debug.LogWarning("[QvPenLoader] No drawing data found in JSON");
                return false;
            }

            DrawImportedData(data);
        
            Debug.Log("[QvPenLoader] Successfully imported " + exportedCount + " drawing objects");
            UpdateStatusUI("インポート成功！", Color.green);
            return true;
        }

        /// <summary>
        /// JSONデータから描画データを解析して線を生成する
        /// </summary>
        private void DrawImportedData(string jsonData)
        {
            if (!isInitialized || string.IsNullOrEmpty(jsonData))
            {
                return;
            }

            // JSONデータをVRChatのDataToken形式に変換
            DataToken dataToken;
            if (!VRCJson.TryDeserializeFromJson(jsonData, out dataToken) || 
                dataToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[QvPenLoader] JSONデータの解析に失敗しました");
                return;
            }

            DataDictionary jsonDataDict = dataToken.DataDictionary;
            DataToken exportedDataToken;
            
            // 描画データの配列を取得
            if (!jsonDataDict.TryGetValue("exportedData", TokenType.DataList, out exportedDataToken))
            {
                Debug.LogError("[QvPenLoader] exportedDataが見つかりません");
                return;
            }

            DataList exportedData = exportedDataToken.DataList;
            int exportedCount = exportedData.Count;
            
            if (exportedCount == 0)
            {
                Debug.LogWarning("No drawing data found in JSON");
                return;
            }

            // ピックアップモードかどうかを判定
            bool usePickup = pickupToggle != null && pickupToggle.isOn && fixedPickupObject != null;

            // 全ストロークの平均中心位置を計算（ピックアップモード用）
            Vector3 globalCenter = Vector3.zero;
            int totalPoints = 0;

            // ピックアップモードの場合のみ平均位置を計算
            if (usePickup)
            {
                // 最初のパスで全ポイントの平均位置を計算
                for (int i = 0; i < exportedData.Count; i++)
                {
                    if (exportedData.TryGetValue(i, TokenType.DataDictionary, out DataToken drawingDataToken))
                    {
                        DataDictionary drawingData = drawingDataToken.DataDictionary;
                        if (drawingData.TryGetValue("positions", TokenType.DataList, out DataToken positionsToken))
                        {
                            DataList positionsList = positionsToken.DataList;
                            int posCount = positionsList.Count;
                            if (posCount % 3 == 0 && posCount >= 6)
                            {
                                int vertexCount = posCount / 3;
                                for (int j = 0; j < vertexCount; j++)
                                {
                                    Vector3 pos;
                                    if (TryGetPosition(positionsList, j * 3, out pos))
                                    {
                                        globalCenter += pos;
                                        totalPoints++;
                                    }
                                }
                            }
                        }
                    }
                }

                // 平均位置を計算
                if (totalPoints > 0)
                {
                    globalCenter /= totalPoints;
                }
                
                // ピックアップモードの場合、ピックアップオブジェクトをアクティブにする
                // (UpdatePickupObjectState内で位置と回転がリセットされる)
                UpdatePickupObjectState(true);
                if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
                {
                    RequestSerialization(); // オーナーの場合は状態を同期
                }
            }

            // 各描画データを処理
            for (int i = 0; i < exportedData.Count; i++)
            {
                if (exportedData.TryGetValue(i, TokenType.DataDictionary, out DataToken drawingDataToken))
                {
                    if (usePickup)
                    {
                        // ピックアップモード：線をピックアップオブジェクト内の親に生成
                        CreateLineInContainer(drawingDataToken.DataDictionary, pickupLineParent, globalCenter);
                    }
                    else
                    {
                        // 通常モード：InkParent直下に線を生成
                        CreateLineInContainer(drawingDataToken.DataDictionary, inkParent, Vector3.zero);
                    }
                }
                else
                {
                    Debug.LogWarning($"[QvPenLoader] 描画データ {i} の形式が不正です");
                }
            }
        
            Debug.Log("[QvPenLoader] Successfully imported " + exportedCount + " drawing objects");
            UpdateStatusUI("インポート成功！", Color.green);
        }

        /// <summary>
        /// 描画データから線を生成し、指定されたコンテナの子にする
        /// </summary>
        private void CreateLineInContainer(DataDictionary drawingData, Transform container, Vector3 globalCenter)
        {
            // カラー情報の取得と検証
            if (!drawingData.TryGetValue("color", TokenType.DataDictionary, out DataToken colorToken))
            {
                Debug.LogError("[QvPenLoader] カラー情報が見つかりません");
                return;
            }

            DataDictionary colorData = colorToken.DataDictionary;
            if (!colorData.TryGetValue("type", TokenType.String, out DataToken colorTypeToken) ||
                !colorData.TryGetValue("value", TokenType.DataList, out DataToken colorValuesToken))
            {
                Debug.LogError("[QvPenLoader] カラーデータの形式が不正です");
                return;
            }

            // グラデーションの判定とカラー配列の作成
            bool isGradient = colorTypeToken.String == "gradient";
            DataList colorValues = colorValuesToken.DataList;
            if (colorValues.Count == 0)
            {
                Debug.LogError("[QvPenLoader] カラー値が存在しません");
                return;
            }

            Color[] colors = ParseColors(colorValues);
            if (colors == null) return;

            // 線の太さの取得
            float lineWidth = GetLineWidth(drawingData);

            // 位置データの取得と検証
            if (!drawingData.TryGetValue("positions", TokenType.DataList, out DataToken positionsToken))
            {
                Debug.LogError("[QvPenLoader] 位置データが見つかりません");
                return;
            }

            Vector3[] positions = ParsePositions(positionsToken.DataList);
            if (positions == null) return;

            // 位置調整（スケーリングを適用）
            Vector3[] adjustedPositions;
            
            // ピックアップモードでグローバル中心がある場合とそれ以外で位置調整方法を変える
            if (globalCenter != null && globalCenter != Vector3.zero)
            {
                // すべての線の中心を考慮して位置を調整（ピックアップモード）
                adjustedPositions = new Vector3[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    // 中心位置からのオフセットにスケールを適用
                    Vector3 offset = (positions[i] - globalCenter) * currentScale;
                    adjustedPositions[i] = globalCenter + offset - globalCenter;
                }
            }
            else
            {
                // 通常モード：InkParentのワールド位置を考慮して位置を調整
                adjustedPositions = new Vector3[positions.Length];
                for (int i = 0; i < positions.Length; i++)
                {
                    // ローカル位置にスケールを適用
                    Vector3 localPos = (positions[i] - inkParent.position) * currentScale;
                    adjustedPositions[i] = localPos;
                }
            }

            // 線の太さにもスケールを適用
            float scaledLineWidth = lineWidth * currentScale;

            // LineRendererオブジェクトを生成
            GameObject lineObj = CreateLineObject(scaledLineWidth);
            if (lineObj == null) return;
            
            // コンテナの子にする
            lineObj.transform.SetParent(container, false);
            lineObj.transform.localPosition = Vector3.zero;
            lineObj.transform.localRotation = Quaternion.identity;

            LineRenderer lineRenderer = lineObj.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                Debug.LogError("[QvPenLoader] LineRendererコンポーネントが見つかりません");
                Destroy(lineObj);
                return;
            }

            // 線の基本設定（スケールされた太さを使用）
            SetupLineRenderer(lineRenderer, adjustedPositions, scaledLineWidth);

            // カラー設定
            ApplyLineColors(lineRenderer, colors, isGradient);

            lineObj.layer = lineLayer;
        }

        /// <summary>
        /// カラー配列をパースする
        /// </summary>
        private Color[] ParseColors(DataList colorValues)
        {
            Color[] colors = new Color[colorValues.Count];
            for (int i = 0; i < colorValues.Count; i++)
            {
                if (!colorValues.TryGetValue(i, TokenType.String, out DataToken colorHexToken))
                {
                    Debug.LogError($"[QvPenLoader] Invalid color format at index {i}");
                    return null;
                }

                if (!TryParseHexColor(colorHexToken.String, out colors[i]))
                {
                    Debug.LogError($"[QvPenLoader] Failed to parse color value: {colorHexToken.String}");
                    return null;
                }
            }
            return colors;
        }

        /// <summary>
        /// 線の太さを取得する
        /// </summary>
        private float GetLineWidth(DataDictionary drawingData)
        {
            float lineWidth = defaultWidth;
            if (drawingData.TryGetValue("width", TokenType.Float, out DataToken widthToken))
            {
                lineWidth = widthToken.Float;
            }
            else if (drawingData.TryGetValue("width", TokenType.Double, out DataToken widthDoubleToken))
            {
                lineWidth = (float)widthDoubleToken.Double;
            }

            if (lineWidth <= 0)
            {
                Debug.LogWarning($"[QvPenLoader] Invalid line width ({lineWidth}), using default: {defaultWidth}");
                lineWidth = defaultWidth;
            }

            return lineWidth;
        }

        /// <summary>
        /// 位置データ配列をパースする
        /// </summary>
        private Vector3[] ParsePositions(DataList positionsList)
        {
            int posCount = positionsList.Count;
            if (posCount % 3 != 0 || posCount < 6)
            {
                Debug.LogError("[QvPenLoader] Invalid position data: not a multiple of 3 or too few points");
                return null;
            }

            int vertexCount = posCount / 3;
            Vector3[] positions = new Vector3[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 pos;
                if (!TryGetPosition(positionsList, i * 3, out pos))
                {
                    return null;
                }
                positions[i] = pos;
            }

            return positions;
        }

        /// <summary>
        /// 位置データを取得する
        /// </summary>
        private bool TryGetPosition(DataList positionsList, int startIndex, out Vector3 position)
        {
            position = Vector3.zero;
            float[] values = new float[3];

            for (int i = 0; i < 3; i++)
            {
                if (!positionsList.TryGetValue(startIndex + i, TokenType.Float, out DataToken token) &&
                    !positionsList.TryGetValue(startIndex + i, TokenType.Double, out token))
                {
                    Debug.LogError($"[QvPenLoader] 位置データの取得に失敗: インデックス {startIndex + i}");
                    return false;
                }

                values[i] = token.TokenType == TokenType.Float ? token.Float : (float)token.Double;
            }

            position = new Vector3(values[0], values[1], values[2]);
            return true;
        }

        private bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.white;
        
            if (string.IsNullOrEmpty(hex))
                return false;
        
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);
        
            if (hex.Length != 6)
                return false;
        
            byte r = 0, g = 0, b = 0;
            bool success = true;
        
            success &= TryParseHexByte(hex.Substring(0, 2), out r);
            success &= TryParseHexByte(hex.Substring(2, 2), out g);
            success &= TryParseHexByte(hex.Substring(4, 2), out b);
        
            if (!success)
                return false;
        
            color = new Color(r / 255f, g / 255f, b / 255f);
            return true;
        }

        private bool TryParseHexByte(string hex, out byte value)
        {
            value = 0;
            int result = 0;
        
            for (int i = 0; i < hex.Length; i++)
            {
                result *= 16;
                char c = hex[i];
            
                if (c >= '0' && c <= '9')
                    result += c - '0';
                else if (c >= 'a' && c <= 'f')
                    result += 10 + (c - 'a');
                else if (c >= 'A' && c <= 'F')
                    result += 10 + (c - 'A');
                else
                    return false;
            }
        
            if (result < 0 || result > 255)
                return false;
        
            value = (byte)result;
            return true;
        }

        /// <summary>
        /// 線オブジェクトを生成する
        /// </summary>
        private GameObject CreateLineObject(float lineWidth)
        {
            GameObject lineObj = Instantiate(lineRendererPrefab);
            if (lineObj == null)
            {
                Debug.LogError("[QvPenLoader] LineRenderer prefabの生成に失敗しました");
                UpdateStatusUI("エラー: LineRendererの生成に失敗しました", Color.red);
                return null;
            }

            lineObj.name = $"Line_Width{lineWidth:F3}";
            return lineObj;
        }

        /// <summary>
        /// LineRendererの基本設定を行う
        /// </summary>
        private void SetupLineRenderer(LineRenderer lineRenderer, Vector3[] positions, float lineWidth)
        {
            lineRenderer.positionCount = positions.Length;
            lineRenderer.SetPositions(positions);

            // 線の太さ設定
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 0f);
            curve.AddKey(1f, 0f);
            lineRenderer.widthCurve = curve;
            lineRenderer.widthMultiplier = 0f;

            if (defaultMaterial != null)
            {
                lineRenderer.material = defaultMaterial;
                lineRenderer.material.SetFloat("_Width", lineWidth);
            }
            else
            {
                Debug.LogError($"[QvPenLoader] マテリアルが設定されていません（太さ: {lineWidth}）");
            }
        }

        /// <summary>
        /// 線の色を設定する
        /// </summary>
        private void ApplyLineColors(LineRenderer lineRenderer, Color[] colors, bool isGradient)
        {
            if (isGradient && colors.Length > 1)
            {
                ApplyGradientColors(lineRenderer, colors);
            }
            else if (colors.Length > 0)
            {
                lineRenderer.startColor = colors[0];
                lineRenderer.endColor = colors[0];
            }
        }

        /// <summary>
        /// グラデーションカラーを設定する
        /// </summary>
        private void ApplyGradientColors(LineRenderer lineRenderer, Color[] colors)
        {
            Gradient gradient = new Gradient();
            GradientColorKey[] colorKeys = new GradientColorKey[colors.Length];
            GradientAlphaKey[] alphaKeys = new GradientAlphaKey[colors.Length];

            for (int i = 0; i < colors.Length; i++)
            {
                float time = i / (float)(colors.Length - 1);
                colorKeys[i] = new GradientColorKey(colors[i], time);
                alphaKeys[i] = new GradientAlphaKey(1.0f, time);
            }

            gradient.SetKeys(colorKeys, alphaKeys);
            lineRenderer.colorGradient = gradient;
        }

        public void ClearAllImportedData()
        {
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            foreach (Transform child in inkParent)
            {
                Destroy(child.gameObject);
            }
            
            // ピックアップオブジェクト内のデータもクリア
            if (pickupLineParent != null)
            {
                foreach (Transform child in pickupLineParent)
                {
                    Destroy(child.gameObject);
                }
            }
            
            // ピックアップオブジェクトを非アクティブ化
            UpdatePickupObjectState(false);

            syncedUrl = null;
            hasImportedInWorld = false;
            lastLoadedUrl = null;
            initialLoadExecuted = false;
            RequestSerialization();

            UpdateStatusUI("すべてのデータを削除しました", Color.white);
        }

        private void Update()
        {
            if (isLoading && (Time.time - loadStartTime) > LOAD_TIMEOUT_SECONDS)
            {
                StopLoading();
                UpdateStatusUI($"タイムアウト: 読み込みに失敗しました", Color.red);
                Debug.LogError($"[QvPenLoader] Load operation timed out after {LOAD_TIMEOUT_SECONDS} seconds");
            }
        }

        public Transform[] GetImportedLines()
        {
            if (inkParent == null) return new Transform[0];

            Transform[] lines = new Transform[inkParent.childCount];
            for (int i = 0; i < inkParent.childCount; i++)
            {
                lines[i] = inkParent.GetChild(i);
            }
            return lines;
        }

        public void DeleteImportedGroup(string groupId)
        {
            if (string.IsNullOrEmpty(groupId))
            {
                Debug.LogWarning("[QvPenLoader] Empty group ID provided for deletion");
                return;
            }

            Transform foundGroup = null;
            foreach (Transform child in inkParent)
            {
                if (child.name == groupId || 
                    child.name == $"Line_Width{groupId}")
                {
                    foundGroup = child;
                    break;
                }
            }

            if (foundGroup != null)
            {
                Debug.Log($"[QvPenLoader] Deleting line: {foundGroup.name}");
                Destroy(foundGroup.gameObject);
            }
            else
            {
                Debug.LogWarning($"[QvPenLoader] Line not found with ID: {groupId}");
            }
        }

        public void CheckAndRestoreData()
        {
            if (lastLoadedUrl != null && !string.IsNullOrEmpty(lastLoadedUrl.Get()))
            {
                Debug.Log("[QvPenLoader] Attempting to reload previous URL");
                LoadFromUrl(lastLoadedUrl);
            }
            else
            {
                Debug.Log("[QvPenLoader] No previous URL found to restore");
                UpdateStatusUI("復元するデータがありません", Color.yellow);
            }
        }

        // バウンディングサイズを計算する
        private float CalculateBoundingSizeFromJson(string jsonData)
        {
            // JSONをパース
            DataToken dataToken;
            bool parseSuccess = VRCJson.TryDeserializeFromJson(jsonData, out dataToken);
            
            if (!parseSuccess)
            {
                Debug.LogError("[QvPenLoader] Failed to parse JSON data");
                return 0f;
            }
            
            if (dataToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError($"[QvPenLoader] Invalid JSON root type: {dataToken.TokenType}");
                return 0f;
            }

            DataDictionary jsonDataDict = dataToken.DataDictionary;
            
            // JSONの構造をログ出力
            DataList keysList = jsonDataDict.GetKeys();
            int keysCount = keysList.Count;
            string[] keys = new string[keysCount];
            for (int i = 0; i < keysCount; i++)
            {
                if (keysList.TryGetValue(i, TokenType.String, out DataToken keyToken))
                {
                    keys[i] = keyToken.String;
                }
            }
            Debug.Log($"[QvPenLoader] JSON root keys: {string.Join(", ", keys)}");
            
            // 描画データの配列を取得
            bool hasExportedData = jsonDataDict.TryGetValue("exportedData", TokenType.DataList, out DataToken exportedDataToken);
            if (!hasExportedData)
            {
                Debug.LogError("[QvPenLoader] exportedData not found in JSON");
                return 0f;
            }

            DataList exportedData = exportedDataToken.DataList;
            int exportedCount = exportedData.Count;
            
            Debug.Log($"[QvPenLoader] Processing data for size check");
            
            if (exportedCount == 0)
            {
                Debug.LogWarning("[QvPenLoader] No drawing data found in JSON");
                return 0f;
            }

            // すべての点の最小・最大座標を計算
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool hasPoints = false;
            int totalPointsProcessed = 0;

            // 各描画データを解析
            for (int i = 0; i < exportedData.Count; i++)
            {
                if (!exportedData.TryGetValue(i, TokenType.DataDictionary, out DataToken drawingDataToken))
                {
                    Debug.LogWarning($"[QvPenLoader] Drawing data at index {i} is not a dictionary");
                    continue;
                }
                
                DataDictionary drawingData = drawingDataToken.DataDictionary;
                
                // 位置データの取得試行
                bool hasPositions = drawingData.TryGetValue("positions", TokenType.DataList, out DataToken positionsToken);
                if (!hasPositions)
                {
                    Debug.LogWarning($"[QvPenLoader] Drawing data at index {i} has no positions");
                    continue;
                }
                
                DataList positionsList = positionsToken.DataList;
                int posCount = positionsList.Count;
                
                // 位置データが正しいフォーマットかチェック
                if (posCount < 3) // 少なくとも1点が必要
                {
                    Debug.LogWarning($"[QvPenLoader] Drawing data at index {i} has too few positions: {posCount}");
                    continue;
                }
                
                bool isXYZ = posCount % 3 == 0;
                int pointCount = isXYZ ? posCount / 3 : posCount / 2; // XYZ or XY形式を想定
                
                Debug.Log($"[QvPenLoader] Drawing {i}: Format is {(isXYZ ? "XYZ" : "XY")}, points={pointCount}");
                
                if (pointCount < 1)
                {
                    Debug.LogWarning($"[QvPenLoader] Drawing {i} has no valid points");
                    continue;
                }
                
                int pointsInThisDrawing = 0;
                
                // 各点の処理
                for (int j = 0; j < pointCount; j++)
                {
                    Vector3 pos = Vector3.zero; // 初期化を追加
                    bool gotPosition = false;
                    
                    // XYZ形式
                    if (isXYZ)
                    {
                        gotPosition = TryGetPosition(positionsList, j * 3, out pos);
                    }
                    // XY形式（Z=0と仮定）
                    else if (posCount % 2 == 0)
                    {
                        float x = 0, y = 0;
                        bool gotX = positionsList.TryGetValue(j * 2, TokenType.Float, out DataToken xToken) || 
                                    positionsList.TryGetValue(j * 2, TokenType.Double, out xToken);
                        bool gotY = positionsList.TryGetValue(j * 2 + 1, TokenType.Float, out DataToken yToken) || 
                                    positionsList.TryGetValue(j * 2 + 1, TokenType.Double, out yToken);
                        
                        if (gotX && gotY)
                        {
                            x = xToken.TokenType == TokenType.Float ? xToken.Float : (float)xToken.Double;
                            y = yToken.TokenType == TokenType.Float ? yToken.Float : (float)yToken.Double;
                            pos = new Vector3(x, y, 0);
                            gotPosition = true;
                        }
                    }
                    
                    if (!gotPosition)
                    {
                        continue;
                    }
                    
                    // スケール値を考慮
                    pos *= currentScale;
                    
                    // 最小・最大値を更新
                    min.x = Mathf.Min(min.x, pos.x);
                    min.y = Mathf.Min(min.y, pos.y);
                    min.z = Mathf.Min(min.z, pos.z);
                    
                    max.x = Mathf.Max(max.x, pos.x);
                    max.y = Mathf.Max(max.y, pos.y);
                    max.z = Mathf.Max(max.z, pos.z);
                    
                    hasPoints = true;
                    pointsInThisDrawing++;
                }
                
                totalPointsProcessed += pointsInThisDrawing;
                Debug.Log($"[QvPenLoader] Drawing {i}: Processed {pointsInThisDrawing} points successfully");
            }

            Debug.Log($"[QvPenLoader] Total points processed: {totalPointsProcessed}");
            
            if (!hasPoints)
            {
                Debug.LogWarning("[QvPenLoader] No valid points found in the drawing data");
                return 0f;
            }

            // バウンディングボックスの最大辺の長さを計算
            Vector3 size = max - min;
            float maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            // 詳細なログ出力
            Debug.Log($"[QvPenLoader] Bounding box dimensions: min=({min.x:F2}, {min.y:F2}, {min.z:F2}), max=({max.x:F2}, {max.y:F2}, {max.z:F2})");
            Debug.Log($"[QvPenLoader] Final size: {size.x:F2}m x {size.y:F2}m x {size.z:F2}m");
            Debug.Log($"[QvPenLoader] Maximum dimension: {maxDimension:F2}m (Scale factor: {currentScale:F2})");
            
            if (maxDimension > MAX_BOUNDING_SIZE)
            {
                Debug.LogWarning($"[QvPenLoader] Size check failed: {maxDimension:F2}m exceeds limit of {MAX_BOUNDING_SIZE:F2}m");
            }
            else
            {
                Debug.Log($"[QvPenLoader] Size check passed: {maxDimension:F2}m is within limit of {MAX_BOUNDING_SIZE:F2}m");
            }
            
            return maxDimension;
        }

        /// <summary>
        /// すべての状態を強制的にリセットする
        /// UI ボタンから呼び出されることを想定
        /// </summary>
        public void ResetEverything()
        {
            // オーナー権限を強制的に取得
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // ローディング状態をリセット
            isLoading = false;
            
            // インポート中フラグをリセット
            isImporting = false;

            // インクデータをクリア
            foreach (Transform child in inkParent)
            {
                Destroy(child.gameObject);
            }
            
            // ピックアップオブジェクト内のデータもクリア
            if (pickupLineParent != null)
            {
                foreach (Transform child in pickupLineParent)
                {
                    Destroy(child.gameObject);
                }
            }
            
            // ピックアップオブジェクトを非アクティブ化して位置をリセット
            UpdatePickupObjectState(false);
            
            // 同期変数をリセット
            syncedUrl = null;
            hasImportedInWorld = false;
            lastLoadedUrl = null;
            initialLoadExecuted = false;
            
            // 状態を全プレイヤーに同期
            RequestSerialization();
            
            // UI表示を更新
            UpdateStatusUI("すべてのデータをリセットしました", Color.cyan);
            
            // 全プレイヤーにリセット通知を送信
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(OnResetEventReceived));
        }

        /// <summary>
        /// リセットイベントを受信した時の処理
        /// </summary>
        public void OnResetEventReceived()
        {
            // ローディング状態をリセット
            StopLoading();
            
            // ローカルUI更新
            UpdateStatusUI("データがリセットされました", Color.cyan);
        }

        /// <summary>
        /// InputFieldからスケール値を取得する
        /// </summary>
        public void UpdateScaleFromInput()
        {
            if (scaleInputField == null)
            {
                Debug.LogWarning("[QvPenLoader] Scale input field is null");
                return;
            }
            
            float scale;
            bool parseSuccess = float.TryParse(scaleInputField.text, out scale);
            
            if (parseSuccess && scale > 0)
            {
                float oldScale = currentScale;
                currentScale = scale;
                Debug.Log($"[QvPenLoader] Scale updated from {oldScale:F2} to {currentScale:F2}");
                
                // スケールが変わった場合、前回のサイズチェック結果をクリア
                if (Mathf.Abs(oldScale - currentScale) > 0.001f)
                {
                    Debug.Log($"[QvPenLoader] Scale changed significantly, clearing previous size check results");
                    _approvedUrl = null;
                    
                    // スケール変更をUIで明示
                    UpdateStatusUI($"スケール: {currentScale:F2}に変更しました。次回インポート時にサイズをチェックします", Color.cyan);
                }
            }
            else
            {
                // 変換できない値や負の値が入力された場合はデフォルト値に戻す
                Debug.LogWarning($"[QvPenLoader] Invalid scale value: '{scaleInputField.text}', resetting to 1.0");
                currentScale = 1.0f;
                scaleInputField.text = "1.0";
                
                // 無効な値が入力されたことをUIで表示
                UpdateStatusUI("無効なスケール値です。1.0にリセットしました", Color.yellow);
                
                // サイズチェック結果をリセット
                _approvedUrl = null;
            }
        }

        private void DebugLog(string message)
        {
#if UNITY_EDITOR
            Debug.Log(message);
#endif
        }

        private void DebugLogWarning(string message)
        {
#if UNITY_EDITOR
            Debug.LogWarning(message);
#endif
        }

        private void DebugLogError(string message)
        {
#if UNITY_EDITOR
            Debug.LogError(message);
#endif
        }
    }
}