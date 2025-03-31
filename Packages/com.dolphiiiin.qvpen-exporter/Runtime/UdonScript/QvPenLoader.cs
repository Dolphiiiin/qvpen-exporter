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

namespace QvPenExporter.UdonScript
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

            // 初期状態の設定
            isInitialized = true;
            UpdateStatusUI("準備完了", Color.white);
            Debug.Log("[QvPenLoader] Initialized successfully");

            // 既にワールドでインポートされている場合は初期ロードを実行
            if (hasImportedInWorld && !initialLoadExecuted && syncedUrl != null)
            {
                initialLoadExecuted = true;
                StartImport(syncedUrl);
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

            Debug.Log($"[QvPenLoader] Starting load from URL: {url.Get()}");
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
            if (!string.IsNullOrEmpty(jsonData))
            {
                ProcessImportData(jsonData);
                UpdateStatusUI("インポート完了！", Color.green);

                // 同期フラグを更新
                isImporting = false;
                if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
                {
                    RequestSerialization();
                }
            }
            else
            {
                Debug.LogError("[QvPenLoader] Received empty data");
                UpdateStatusUI("エラー: データが空です", Color.red);
                
                // エラー時はインポート状態をリセット
                isImporting = false;
                if (Networking.IsOwner(Networking.LocalPlayer, gameObject))
                {
                    RequestSerialization();
                }
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

        private void ProcessImportData(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
                Debug.LogError("[QvPenLoader] Loaded data is empty");
                return;
            }

            DataToken dataToken;
            bool success = VRCJson.TryDeserializeFromJson(data, out dataToken);
            if (!success || dataToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[QvPenLoader] Failed to parse JSON data");
                return;
            }
        
            DataDictionary jsonData = dataToken.DataDictionary;
        
            if (!jsonData.TryGetValue("exportedData", TokenType.DataList, out DataToken exportedDataToken))
            {
                Debug.LogError("JSON data does not contain exportedData array");
                return;
            }
        
            DataList exportedData = exportedDataToken.DataList;
            int exportedCount = exportedData.Count;
        
            if (exportedCount == 0)
            {
                Debug.LogWarning("No drawing data found in JSON");
                return;
            }

            DrawImportedData(data);
        
            Debug.Log("[QvPenLoader] Successfully imported " + exportedCount + " drawing objects");
            UpdateStatusUI("インポート成功！", Color.green);
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

            // 各描画データを処理
            DataList exportedData = exportedDataToken.DataList;
            for (int i = 0; i < exportedData.Count; i++)
            {
                if (exportedData.TryGetValue(i, TokenType.DataDictionary, out DataToken drawingDataToken))
                {
                    CreateLineFromData(drawingDataToken.DataDictionary);
                }
                else
                {
                    Debug.LogWarning($"[QvPenLoader] 描画データ {i} の形式が不正です");
                }
            }
        }

        /// <summary>
        /// 描画データから線を生成する
        /// </summary>
        private void CreateLineFromData(DataDictionary drawingData)
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

            // 線の生成
            CreateLine(positions, colors, isGradient, lineWidth);
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
                    Debug.LogError($"[QvPenLoader] カラー値 {i} の形式が不正です");
                    return null;
                }

                if (!TryParseHexColor(colorHexToken.String, out colors[i]))
                {
                    Debug.LogError($"[QvPenLoader] カラー値のパースに失敗: {colorHexToken.String}");
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
                Debug.LogWarning($"[QvPenLoader] 無効な線の太さ ({lineWidth})、デフォルト値を使用: {defaultWidth}");
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
                Debug.LogError("[QvPenLoader] 位置データが不正: 3の倍数でないか、点が少なすぎます");
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
        /// 線を生成する
        /// </summary>
        private void CreateLine(Vector3[] positions, Color[] colors, bool isGradient, float lineWidth)
        {
            if (!ValidateLineParameters(positions, colors)) return;

            // InkParentのワールド位置を考慮して位置を調整
            Vector3[] adjustedPositions = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                // InkParentのワールド位置を相殺
                adjustedPositions[i] = positions[i] - inkParent.position;
            }

            GameObject lineObj = CreateLineObject(lineWidth);
            if (lineObj == null) return;

            LineRenderer lineRenderer = lineObj.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                Debug.LogError("[QvPenLoader] LineRendererコンポーネントが見つかりません");
                Destroy(lineObj);
                return;
            }

            // 線の基本設定（調整済みの位置を使用）
            SetupLineRenderer(lineRenderer, adjustedPositions, lineWidth);

            // カラー設定
            ApplyLineColors(lineRenderer, colors, isGradient);

            lineObj.layer = lineLayer;
            Debug.Log($"[QvPenLoader] {positions.Length}点の線を生成しました（太さ: {lineWidth:F3}）");
        }

        /// <summary>
        /// 線のパラメータを検証する
        /// </summary>
        private bool ValidateLineParameters(Vector3[] positions, Color[] colors)
        {
            if (positions == null || positions.Length < 2)
            {
                Debug.LogWarning("[QvPenLoader] 点が少なすぎます");
                return false;
            }

            if (colors == null || colors.Length == 0)
            {
                Debug.LogWarning("[QvPenLoader] カラーデータが不正です");
                return false;
            }

            if (lineRendererPrefab == null)
            {
                Debug.LogError("[QvPenLoader] LineRenderer prefabが設定されていません");
                return false;
            }

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

            lineObj.transform.SetParent(inkParent, false);
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
                Debug.Log("[QvPenLoader] Reloading last URL");
                LoadFromUrl(lastLoadedUrl);
            }
            else
            {
                Debug.Log("[QvPenLoader] No previous URL to restore");
                UpdateStatusUI("復元するデータがありません", Color.yellow);
            }
        }
    }
}