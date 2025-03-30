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
    
        [SerializeField]
        private GameObject loadingIndicator;

        private float loadStartTime;
        private bool isLoading = false;
        private const float LOAD_TIMEOUT_SECONDS = 30.0f;

        private bool isInitialized = false;

        [Header("Material Settings")]
        [SerializeField]
        private Material[] widthMaterials;
        private const int MAX_WIDTH_MATERIALS = 10;

        private VRCUrl lastLoadedUrl;

        [UdonSynced]
        private VRCUrl syncedUrl;

        [UdonSynced]
        private bool hasLoadedData = false;

        [UdonSynced]
        private bool isImporting = false; // インポート中フラグ

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

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }

            widthMaterials = new Material[MAX_WIDTH_MATERIALS];
            if (defaultMaterial != null)
            {
                for (int i = 0; i < MAX_WIDTH_MATERIALS; i++)
                {
                    widthMaterials[i] = defaultMaterial;
                }
            }

            UpdateStatusUI("準備完了", Color.white);
            isInitialized = true;
            Debug.Log("[QvPenLoader] Initialized successfully");
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

            LoadFromUrl(importUrl);
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

            // オーナー権限を取得
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // 既存のデータをクリア
            ClearExistingData();

            lastLoadedUrl = url;
            syncedUrl = url;
            hasLoadedData = false;
            isImporting = true;
            RequestSerialization();

            StartLoading();
            Debug.Log($"[QvPenLoader] Starting load from URL: {url.Get()}");
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
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
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(true);
            }
        }

        private void StopLoading()
        {
            isLoading = false;
            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
            }
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
            }
            else
            {
                Debug.LogError("[QvPenLoader] Received empty data");
                UpdateStatusUI("エラー: データが空です", Color.red);
            }

            // ロード完了を同期
            hasLoadedData = true;
            isImporting = false;
            RequestSerialization();
            StopLoading();
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            float loadTime = Time.time - loadStartTime;
            Debug.LogError($"[QvPenLoader] StringLoad error after {loadTime:F2} seconds: {result.ErrorCode} - {result.Error}");
            
            UpdateStatusUI("エラー: 読み込みに失敗しました", Color.red);

            // エラー時は他のプレイヤーがインポートできるようにフラグを解除
            isImporting = false;
            RequestSerialization();
            StopLoading();
        }

        public override void OnDeserialization()
        {
            if (hasLoadedData && syncedUrl != null && !string.IsNullOrEmpty(syncedUrl.Get()))
            {
                if (inkParent.childCount == 0)
                {
                    Debug.Log("[QvPenLoader] Loading synced data...");
                    lastLoadedUrl = syncedUrl;
                    LoadFromUrl(syncedUrl);
                }
            }
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (player.isLocal && hasLoadedData && syncedUrl != null && !string.IsNullOrEmpty(syncedUrl.Get()))
            {
                Debug.Log("[QvPenLoader] Loading existing data as new player...");
                LoadFromUrl(syncedUrl);
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

        private void DrawImportedData(string jsonData)
        {
            if (!isInitialized || string.IsNullOrEmpty(jsonData))
            {
                return;
            }
        
            DataToken dataToken;
            bool success = VRCJson.TryDeserializeFromJson(jsonData, out dataToken);
            if (!success || dataToken.TokenType != TokenType.DataDictionary)
            {
                Debug.LogError("[QvPenLoader] Failed to parse JSON data");
                return;
            }
        
            DataDictionary jsonDataDict = dataToken.DataDictionary;
        
            if (!jsonDataDict.TryGetValue("exportedData", TokenType.DataList, out DataToken exportedDataToken))
            {
                Debug.LogError("[QvPenLoader] JSON data does not contain exportedData array");
                return;
            }
        
            DataList exportedData = exportedDataToken.DataList;
            int exportedCount = exportedData.Count;
        
            for (int i = 0; i < exportedCount; i++)
            {
                if (!exportedData.TryGetValue(i, TokenType.DataDictionary, out DataToken drawingDataToken))
                {
                    Debug.LogError("[QvPenLoader] Invalid drawing data at index " + i);
                    continue;
                }
            
                DataDictionary drawingData = drawingDataToken.DataDictionary;
            
                CreateLineFromData(drawingData);
            }
        }

        private void CreateLineFromData(DataDictionary drawingData)
        {
            if (!drawingData.TryGetValue("color", TokenType.DataDictionary, out DataToken colorToken))
            {
                Debug.LogError("[QvPenLoader] Drawing data does not contain color information");
                return;
            }
        
            DataDictionary colorData = colorToken.DataDictionary;
        
            if (!colorData.TryGetValue("type", TokenType.String, out DataToken colorTypeToken))
            {
                Debug.LogError("[QvPenLoader] Color data does not contain type information");
                return;
            }
        
            string colorType = colorTypeToken.String;
            bool isGradient = colorType == "gradient";
        
            if (!colorData.TryGetValue("value", TokenType.DataList, out DataToken colorValuesToken))
            {
                Debug.LogError("[QvPenLoader] Color data does not contain value array");
                return;
            }
        
            DataList colorValues = colorValuesToken.DataList;
            int colorCount = colorValues.Count;
        
            if (colorCount == 0)
            {
                Debug.LogError("[QvPenLoader] No colors found in color data");
                return;
            }
        
            Color[] colors = new Color[colorCount];
            for (int i = 0; i < colorCount; i++)
            {
                if (!colorValues.TryGetValue(i, TokenType.String, out DataToken colorHexToken))
                {
                    Debug.LogError("[QvPenLoader] Invalid color value at index " + i);
                    return;
                }
            
                string colorHex = colorHexToken.String;
                Color parsedColor = Color.white;
                if (!TryParseHexColor(colorHex, out parsedColor))
                {
                    Debug.LogError("[QvPenLoader] Failed to parse color: " + colorHex);
                    return;
                }
            
                colors[i] = parsedColor;
            }
        
            float lineWidth = defaultWidth;
            if (drawingData.TryGetValue("width", TokenType.Float, out DataToken widthToken))
            {
                lineWidth = widthToken.Float;
            }
            else if (drawingData.TryGetValue("width", TokenType.Double, out DataToken widthDoubleToken))
            {
                lineWidth = (float)widthDoubleToken.Double;
            }
            else
            {
                Debug.Log($"[QvPenLoader] Width not defined in JSON, using default width: {defaultWidth}");
            }
            
            if (lineWidth <= 0)
            {
                lineWidth = defaultWidth;
                Debug.LogWarning($"[QvPenLoader] Invalid line width ({lineWidth}), using default: {defaultWidth}");
            }

            if (!drawingData.TryGetValue("positions", TokenType.DataList, out DataToken positionsToken))
            {
                Debug.LogError("[QvPenLoader] Drawing data does not contain positions array");
                return;
            }
        
            DataList positionsList = positionsToken.DataList;
            int posCount = positionsList.Count;
        
            if (posCount % 3 != 0 || posCount < 6)
            {
                Debug.LogError("[QvPenLoader] Invalid position data: not a multiple of 3 or too few points");
                return;
            }
        
            int vertexCount = posCount / 3;
            Vector3[] positions = new Vector3[vertexCount];
        
            for (int i = 0; i < vertexCount; i++)
            {
                float x = 0f, y = 0f, z = 0f;
            
                if (!positionsList.TryGetValue(i * 3, TokenType.Float, out DataToken xToken) &&
                    !positionsList.TryGetValue(i * 3, TokenType.Double, out xToken))
                {
                    Debug.LogError("[QvPenLoader] Invalid X position at index " + (i * 3));
                    return;
                }
            
                if (!positionsList.TryGetValue(i * 3 + 1, TokenType.Float, out DataToken yToken) &&
                    !positionsList.TryGetValue(i * 3 + 1, TokenType.Double, out yToken))
                {
                    Debug.LogError("[QvPenLoader] Invalid Y position at index " + (i * 3 + 1));
                    return;
                }
            
                if (!positionsList.TryGetValue(i * 3 + 2, TokenType.Float, out DataToken zToken) &&
                    !positionsList.TryGetValue(i * 3 + 2, TokenType.Double, out zToken))
                {
                    Debug.LogError("[QvPenLoader] Invalid Z position at index " + (i * 3 + 2));
                    return;
                }
            
                if (xToken.TokenType == TokenType.Float) {
                    x = xToken.Float;
                } else {
                    x = (float)xToken.Double;
                }
            
                if (yToken.TokenType == TokenType.Float) {
                    y = yToken.Float;
                } else {
                    y = (float)yToken.Double;
                }
            
                if (zToken.TokenType == TokenType.Float) {
                    z = zToken.Float;
                } else {
                    z = (float)zToken.Double;
                }
            
                positions[i] = new Vector3(x, y, z);
            }
        
            CreateLine(positions, colors, isGradient, lineWidth);
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

        private void CreateLine(Vector3[] positions, Color[] colors, bool isGradient, float lineWidth)
        {
            if (positions.Length < 2)
            {
                Debug.LogWarning("[QvPenLoader] Not enough points to create line");
                return;
            }

            if (lineRendererPrefab == null)
            {
                Debug.LogError("[QvPenLoader] LineRenderer prefab is not set");
                return;
            }

            GameObject lineObj = Instantiate(lineRendererPrefab);
            if (lineObj == null)
            {
                Debug.LogError("[QvPenLoader] Failed to instantiate LineRenderer prefab");
                UpdateStatusUI("エラー: LineRendererの生成に失敗しました", Color.red);
                return;
            }
            lineObj.transform.SetParent(inkParent, false);
            lineObj.name = $"Line_Width{lineWidth:F3}";
        
            LineRenderer lineRenderer = lineObj.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                lineRenderer.positionCount = positions.Length;
                lineRenderer.SetPositions(positions);
            
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
                    Debug.LogError($"[QvPenLoader] No material available for width {lineWidth}");
                    return;
                }
            
                if (isGradient && colors.Length > 1)
                {
                    Gradient gradient = new Gradient();
                    GradientColorKey[] colorKeys = new GradientColorKey[colors.Length];
                    GradientAlphaKey[] alphaKeys = new GradientAlphaKey[colors.Length];
                
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colorKeys[i] = new GradientColorKey(colors[i], i / (float)(colors.Length - 1));
                        alphaKeys[i] = new GradientAlphaKey(1.0f, i / (float)(colors.Length - 1));
                    }
                
                    gradient.SetKeys(colorKeys, alphaKeys);
                    lineRenderer.colorGradient = gradient;
                }
                else if (colors.Length > 0)
                {
                    lineRenderer.startColor = colors[0];
                    lineRenderer.endColor = colors[0];
                }
            
                lineObj.layer = lineLayer;
                
                Debug.Log($"[QvPenLoader] Created line with {positions.Length} points, width {lineWidth:F3}");
            }
            else
            {
                Debug.LogError("[QvPenLoader] LineRenderer component not found on prefab");
                Destroy(lineObj);
            }
        }

        public void ClearAllImportedData()
        {
            if (isImporting && !Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Debug.LogWarning("[QvPenLoader] Cannot clear while another player is importing");
                UpdateStatusUI("インポート中は削除できません", Color.yellow);
                return;
            }

            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            foreach (Transform child in inkParent)
            {
                Destroy(child.gameObject);
            }

            syncedUrl = null;
            hasLoadedData = false;
            isImporting = false;
            lastLoadedUrl = null;
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