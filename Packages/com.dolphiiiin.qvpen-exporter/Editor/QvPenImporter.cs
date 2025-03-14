#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QvPenExporter.Editor
{
    public class QvPenImporter : EditorWindow
    {
        [Serializable]
        private class LineData
        {
            public ColorData color;
            public List<float> positions;
            public float width; // 直接float型として定義
        }

        [Serializable]
        private class ColorData
        {
            public string type;
            public List<string> value;
        }

        [Serializable]
        private class LineRendererData
        {
            public string timestamp;
            public List<LineData> exportedData;
        }

        private TextAsset jsonFile;
        [SerializeField]
        private GameObject lineRendererPrefab;
        [SerializeField]
        private Material baseMaterial; // ベースとなるInk_PCマテリアル
        private GameObject parentObject;
        private Vector2 scrollPosition;
        private string materialsFolderPath = "Assets/QvPenImporter/Materials";
        private Dictionary<float, Material> widthMaterialCache = new Dictionary<float, Material>();

        [MenuItem("Tools/QvPenImporter")]
        public static void ShowWindow()
        {
            GetWindow<QvPenImporter>("QvPenImporter");
        }

        private void OnEnable()
        {
            // ベースマテリアルの自動検索
            if (baseMaterial == null)
            {
                // プレハブからベースマテリアルを取得
                if (lineRendererPrefab != null)
                {
                    LineRenderer lr = lineRendererPrefab.GetComponent<LineRenderer>();
                    if (lr != null && lr.sharedMaterial != null)
                    {
                        baseMaterial = lr.sharedMaterial;
                    }
                }

                // "QvPen" または "Ink_PC" という名前のマテリアルを検索
                if (baseMaterial == null)
                {
                    string[] guids = AssetDatabase.FindAssets("t:Material Ink_PC");
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        baseMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);
                    }
                }
            }
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.Space(10);
            GUILayout.Label("QvPen Importer", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // 入力フィールド
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                jsonFile = EditorGUILayout.ObjectField("JSONファイル", jsonFile, typeof(TextAsset), false) as TextAsset;
                lineRendererPrefab =
                    EditorGUILayout.ObjectField("LineRenderer Template", lineRendererPrefab, typeof(GameObject), false)
                        as GameObject;
                
                // ベースマテリアル設定
                baseMaterial = EditorGUILayout.ObjectField("ベースマテリアル (Ink_PC)", baseMaterial, typeof(Material), false) as Material;
                
                if (baseMaterial == null)
                {
                    EditorGUILayout.HelpBox("QvPenで使用されているInk_PCマテリアル、または互換性のあるマテリアルを指定してください。", MessageType.Warning);
                }
                else if (!baseMaterial.shader.name.Contains("QvPen") && !baseMaterial.shader.name.Contains("rounded_trail"))
                {
                    EditorGUILayout.HelpBox("選択されたマテリアルはQvPen用のシェーダーを使用していない可能性があります。", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(5);

            // オプション設定
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("オプション", EditorStyles.boldLabel);
                parentObject =
                    EditorGUILayout.ObjectField("親オブジェクト", parentObject, typeof(GameObject), true) as GameObject;
            }

            EditorGUILayout.Space(10);

            // プレビュー情報
            if (jsonFile != null)
            {
                try
                {
                    var data = JsonUtility.FromJson<LineRendererData>(jsonFile.text);
                    
                    // 色ごとにグループ化したディクショナリ
                    var groupedByColor = new Dictionary<string, List<LineData>>();
                    
                    // 色と太さでグループ化したディクショナリ
                    var groupedByColorAndWidth = new Dictionary<string, Dictionary<float, List<LineData>>>();
                    
                    // データを色ごとにグループ化
                    foreach (var line in data.exportedData)
                    {
                        var colorKey = line.color.value[0];
                        var width = line.width;
                        
                        // 色でのグループ化
                        if (!groupedByColor.ContainsKey(colorKey))
                        {
                            groupedByColor[colorKey] = new List<LineData>();
                            groupedByColorAndWidth[colorKey] = new Dictionary<float, List<LineData>>();
                        }
                        groupedByColor[colorKey].Add(line);
                        
                        // 色と太さでのグループ化
                        if (!groupedByColorAndWidth[colorKey].ContainsKey(width))
                        {
                            groupedByColorAndWidth[colorKey][width] = new List<LineData>();
                        }
                        groupedByColorAndWidth[colorKey][width].Add(line);
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField($"タイムスタンプ: {data.timestamp}");
                        EditorGUILayout.LabelField($"合計ライン数: {data.exportedData.Count}", EditorStyles.boldLabel);
                        EditorGUILayout.Space(10);

                        // 色ごとの表示
                        EditorGUILayout.LabelField("【色ごとのグループ】", EditorStyles.boldLabel);
                        foreach (var colorGroup in groupedByColor)
                        {
                            // 色ごとの情報ヘッダー
                            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                            
                            EditorGUILayout.LabelField($"色: #{colorGroup.Key} - ライン数: {colorGroup.Value.Count}", EditorStyles.boldLabel);
                            
                            // 色のプレビュー
                            EditorGUILayout.BeginHorizontal();
                            var rect = EditorGUILayout.GetControlRect(GUILayout.Width(100), GUILayout.Height(16));
                            if (colorGroup.Value[0].color.type == "gradient")
                            {
                                DrawGradient(rect, colorGroup.Value[0].color.value);
                                EditorGUILayout.LabelField(string.Join(", ", colorGroup.Value[0].color.value));
                            }
                            else
                            {
                                Color color;
                                if (ColorUtility.TryParseHtmlString($"#{colorGroup.Key}", out color))
                                {
                                    EditorGUI.DrawRect(rect, color);
                                    EditorGUILayout.LabelField($"#{colorGroup.Key}");
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                            
                            // 線の太さ情報の表示（グループ内のすべての太さを表示）
                            EditorGUILayout.LabelField("線の太さ:", EditorStyles.boldLabel);
                            
                            // グループ内のユニークな線の太さを収集
                            HashSet<float> uniqueWidths = new HashSet<float>();
                            foreach (var lineData in colorGroup.Value)
                            {
                                uniqueWidths.Add(lineData.width);
                            }
                            
                            // 各太さを表示
                            foreach (float width in uniqueWidths)
                            {
                                EditorGUILayout.BeginHorizontal();
                                
                                if (width > 0)
                                {
                                    // この太さのライン数をカウント
                                    int lineCount = colorGroup.Value.Count(l => l.width == width);
                                    
                                    // 太さの数値と数
                                    EditorGUILayout.LabelField($"太さ: {width:F4} ({lineCount}本)", GUILayout.Width(150));
                                    
                                    // 太さの視覚的表現
                                    float displayWidth = Mathf.Clamp(width * 500, 1, 20);
                                    var thicknessRect = EditorGUILayout.GetControlRect(GUILayout.Width(100), GUILayout.Height(displayWidth));
                                    Color previewColor;
                                    ColorUtility.TryParseHtmlString($"#{colorGroup.Key}", out previewColor);
                                    EditorGUI.DrawRect(thicknessRect, previewColor);
                                }
                                else
                                {
                                    EditorGUILayout.LabelField("情報なし（デフォルト値が使用されます）");
                                }
                                
                                EditorGUILayout.EndHorizontal();
                            }
                            
                            EditorGUILayout.EndVertical();
                            EditorGUILayout.Space(5);
                        }
                        
                        // 色と太さの組み合わせごとの表示
                        EditorGUILayout.Space(20);
                        EditorGUILayout.LabelField("【色と太さの組み合わせごとのグループ】", EditorStyles.boldLabel);
                        
                        // 色と太さの組み合わせ単位での表示
                        foreach (var colorKey in groupedByColorAndWidth.Keys)
                        {
                            foreach (var widthKey in groupedByColorAndWidth[colorKey].Keys)
                            {
                                var lines = groupedByColorAndWidth[colorKey][widthKey];
                                
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                                
                                // タイトル
                                EditorGUILayout.LabelField($"色: #{colorKey} 太さ: {widthKey:F4} - ライン数: {lines.Count}", EditorStyles.boldLabel);
                                
                                // 色のプレビュー
                                EditorGUILayout.BeginHorizontal();
                                var rect = EditorGUILayout.GetControlRect(GUILayout.Width(100), GUILayout.Height(16));
                                if (lines[0].color.type == "gradient")
                                {
                                    DrawGradient(rect, lines[0].color.value);
                                    EditorGUILayout.LabelField(string.Join(", ", lines[0].color.value));
                                }
                                else
                                {
                                    Color color;
                                    if (ColorUtility.TryParseHtmlString($"#{colorKey}", out color))
                                    {
                                        EditorGUI.DrawRect(rect, color);
                                        EditorGUILayout.LabelField($"#{colorKey}");
                                    }
                                }
                                EditorGUILayout.EndHorizontal();
                                
                                // 太さの視覚的表現
                                EditorGUILayout.BeginHorizontal();
                                EditorGUILayout.LabelField($"太さ表示:", GUILayout.Width(70));
                                float displayWidth = Mathf.Clamp(widthKey * 500, 1, 20);
                                var thicknessRect = EditorGUILayout.GetControlRect(GUILayout.Width(100), GUILayout.Height(displayWidth));
                                Color previewColor;
                                ColorUtility.TryParseHtmlString($"#{colorKey}", out previewColor);
                                EditorGUI.DrawRect(thicknessRect, previewColor);
                                EditorGUILayout.EndHorizontal();
                                
                                EditorGUILayout.EndVertical();
                                EditorGUILayout.Space(5);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    EditorGUILayout.HelpBox($"無効なJSON形式です: {e.Message}", MessageType.Error);
                    Debug.LogException(e);
                }
            }

            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledGroupScope(jsonFile == null || lineRendererPrefab == null || baseMaterial == null))
            {
                if (GUILayout.Button("インポート", GUILayout.Height(30)))
                {
                    CreateLineRenderers();
                }
            }

            EditorGUILayout.Space(5);

            if (jsonFile == null || lineRendererPrefab == null || baseMaterial == null)
            {
                EditorGUILayout.HelpBox("JSONファイル、ラインレンダラープレハブ、およびベースマテリアルの3つすべてを選択してください。", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        // 指定した線幅に対応するマテリアルを取得または生成
        private Material GetMaterialForWidth(float width)
        {
            // 既に生成済みのマテリアルがあればそれを返す
            if (widthMaterialCache.ContainsKey(width))
            {
                return widthMaterialCache[width];
            }
            
            // マテリアル保存用フォルダの作成
            if (!Directory.Exists(materialsFolderPath))
            {
                Directory.CreateDirectory(materialsFolderPath);
                AssetDatabase.Refresh();
            }
            
            // 新しいマテリアルの名前
            string materialName = $"QvPen_Width_{width:F4}";
            string materialPath = $"{materialsFolderPath}/{materialName}.mat";
            
            // 既存のマテリアルを検索
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            
            if (material == null)
            {
                // 新しいマテリアルを作成
                material = new Material(baseMaterial);
                material.SetFloat("_Width", width);
                
                // マテリアルを保存
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                // 既存のマテリアルの設定を更新
                material.SetFloat("_Width", width);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }
            
            // キャッシュに追加
            widthMaterialCache[width] = material;
            
            return material;
        }

        private void DrawGradient(Rect rect, List<string> colorValues)
        {
            if (colorValues.Count < 2) return;

            var gradientTexture = new Texture2D(colorValues.Count, 1);
            for (int i = 0; i < colorValues.Count; i++)
            {
                Color color;
                if (ColorUtility.TryParseHtmlString($"#{colorValues[i]}", out color))
                {
                    gradientTexture.SetPixel(i, 0, color);
                }
            }
            gradientTexture.Apply();

            GUI.DrawTexture(rect, gradientTexture);
        }

        private void CreateLineRenderers()
        {
            try
            {
                // マテリアルキャッシュをクリア
                widthMaterialCache.Clear();
                
                // JSONデータの解析
                LineRendererData data = JsonUtility.FromJson<LineRendererData>(jsonFile.text);
                
                // 色と太さの組み合わせでグループ化するための辞書
                var groupedByColorAndWidth = new Dictionary<string, List<LineData>>();
                
                // データを色と太さでグループ化
                foreach (var line in data.exportedData)
                {
                    var colorKey = line.color.value[0];
                    var width = line.width;
                    if (width <= 0) width = 0.005f; // デフォルト値を使用
                    
                    // 色と太さを組み合わせたキーを作成
                    var groupKey = $"{colorKey}_width{width:F4}";
                    
                    if (!groupedByColorAndWidth.ContainsKey(groupKey))
                    {
                        groupedByColorAndWidth[groupKey] = new List<LineData>();
                    }
                    groupedByColorAndWidth[groupKey].Add(line);
                }

                // 親オブジェクトの作成または取得
                GameObject parent = parentObject;
                if (parent == null)
                {
                    parent = new GameObject($"Imported QvPen ({data.timestamp})");
                    Undo.RegisterCreatedObjectUndo(parent, "親オブジェクトの作成");
                }

                int linesImported = 0;

                // 色と太さの各組み合わせに対してグループを作成
                foreach (var group in groupedByColorAndWidth)
                {
                    // グループキーから色と太さを抽出
                    string[] parts = group.Key.Split('_');
                    string colorHex = parts[0];
                    string widthStr = parts[1];
                    
                    // グループオブジェクトを作成
                    GameObject groupObject = new GameObject($"Group_{colorHex}_{widthStr}");
                    List<Vector3> groupPositions = new List<Vector3>();

                    foreach (LineData lineData in group.Value)
                    {
                        // プレハブからインスタンスを生成
                        GameObject lineObj = PrefabUtility.InstantiatePrefab(lineRendererPrefab) as GameObject;
                        Undo.RegisterCreatedObjectUndo(lineObj, "ラインレンダラーの作成");

                        LineRenderer lineRenderer = lineObj.GetComponent<LineRenderer>();
                        if (lineRenderer == null)
                        {
                            throw new Exception("プレハブにLineRendererコンポーネントが含まれていません！");
                        }

                        // 位置データの設定
                        int pointCount = lineData.positions.Count / 3;
                        Vector3[] positions = new Vector3[pointCount];
                        Vector3 center = Vector3.zero;

                        for (int i = 0; i < pointCount; i++)
                        {
                            int index = i * 3;
                            positions[i] = new Vector3(
                                lineData.positions[index],
                                lineData.positions[index + 1],
                                lineData.positions[index + 2]
                            );
                            center += positions[i];
                        }

                        center /= pointCount;

                        for (int i = 0; i < pointCount; i++)
                        {
                            positions[i] -= center;
                        }

                        lineRenderer.positionCount = pointCount;
                        lineRenderer.SetPositions(positions);
                        
                        // 線の太さ情報の設定（lineDataから直接取得）
                        float width = lineData.width;
                        if (width <= 0)
                        {
                            width = 0.005f; // デフォルト値
                            Debug.LogWarning("線の太さが0以下のため、デフォルト値を使用します: " + lineObj.name);
                        }
                        
                        // 該当する太さのマテリアルを取得または生成して適用
                        Material widthMaterial = GetMaterialForWidth(width);
                        lineRenderer.material = widthMaterial;
                        
                        // LineRendererの表示幅は常に0に設定（QvPenシェーダーの要件）
                        lineRenderer.widthMultiplier = 0f;
                        
                        // オブジェクト名に太さ情報を追加（グループの情報を利用）
                        lineObj.name = $"LineRenderer_{colorHex}_{widthStr}";

                        // lineObjのTransformを設定
                        lineObj.transform.position = center;
                        lineObj.transform.parent = groupObject.transform;

                        // 色の設定
                        if (lineData.color.type == "gradient")
                        {
                            Gradient gradient = new Gradient();
                            GradientColorKey[] colorKeys = new GradientColorKey[lineData.color.value.Count];
                            for (int i = 0; i < lineData.color.value.Count; i++)
                            {
                                Color color;
                                if (ColorUtility.TryParseHtmlString($"#{lineData.color.value[i]}", out color))
                                {
                                    colorKeys[i].color = color;
                                    colorKeys[i].time = (float)i / (lineData.color.value.Count - 1);
                                }
                            }

                            gradient.colorKeys = colorKeys;
                            lineRenderer.colorGradient = gradient;
                        }
                        else
                        {
                            Color color;
                            if (ColorUtility.TryParseHtmlString($"#{lineData.color.value[0]}", out color))
                            {
                                lineRenderer.startColor = color;
                                lineRenderer.endColor = color;
                            }
                        }

                        groupPositions.Add(center);
                        linesImported++;
                    }

                    // グループの中心を計算して設定
                    Vector3 groupCenter = Vector3.zero;
                    foreach (var pos in groupPositions)
                    {
                        groupCenter += pos;
                    }
                    groupCenter /= groupPositions.Count;

                    foreach (Transform child in groupObject.transform)
                    {
                        child.position -= groupCenter;
                    }
                    groupObject.transform.position = groupCenter;
                    groupObject.transform.parent = parent.transform;
                    Undo.RegisterCreatedObjectUndo(groupObject, "グループオブジェクトの作成");
                }

                // 親オブジェクトの中心を計算して設定
                Vector3 parentCenter = Vector3.zero;
                foreach (Transform child in parent.transform)
                {
                    parentCenter += child.position;
                }
                parentCenter /= parent.transform.childCount;

                foreach (Transform child in parent.transform)
                {
                    child.position -= parentCenter;
                }
                parent.transform.position = parentCenter;

                // マテリアルのアセットを保存
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog("成功", $"ラインレンダラーを{linesImported}個作成しました！\n\n線の太さに応じたマテリアルが {materialsFolderPath} に作成されました。", "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("エラー", $"ラインレンダラーの作成に失敗しました: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }
    }
}
#endif