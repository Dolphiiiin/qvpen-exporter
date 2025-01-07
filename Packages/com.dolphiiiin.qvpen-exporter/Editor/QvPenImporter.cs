#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace QvPenExporter.Editor
{
    public class QvPenImporter : EditorWindow
    {
        [Serializable]
        private class LineData
        {
            public ColorData color;
            public List<float> positions;
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
        private GameObject parentObject;
        private Vector2 scrollPosition;

        [MenuItem("Tools/QvPenImporter")]
        public static void ShowWindow()
        {
            GetWindow<QvPenImporter>("QvPenImporter");
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
                    var groupedData = new Dictionary<string, List<LineData>>();

                    // データを色ごとにグループ化
                    foreach (var line in data.exportedData)
                    {
                        var colorKey = line.color.value[0];
                        if (!groupedData.ContainsKey(colorKey))
                        {
                            groupedData[colorKey] = new List<LineData>();
                        }
                        groupedData[colorKey].Add(line);
                    }

                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField($"タイムスタンプ: {data.timestamp}");

                        foreach (var group in groupedData)
                        {
                            EditorGUILayout.LabelField($"色: #{group.Key} - ライン数: {group.Value.Count}");
                            EditorGUILayout.BeginHorizontal();
                            var rect = EditorGUILayout.GetControlRect(GUILayout.Width(100), GUILayout.Height(16));
                            if (group.Value[0].color.type == "gradient")
                            {
                                DrawGradient(rect, group.Value[0].color.value);
                                EditorGUILayout.LabelField(string.Join(", ", group.Value[0].color.value));
                            }
                            else
                            {
                                Color color;
                                if (ColorUtility.TryParseHtmlString($"#{group.Key}", out color))
                                {
                                    EditorGUI.DrawRect(rect, color);
                                    EditorGUILayout.LabelField($"#{group.Key}");
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
                catch (Exception)
                {
                    EditorGUILayout.HelpBox("無効なJSON形式です", MessageType.Error);
                }
            }

            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledGroupScope(jsonFile == null || lineRendererPrefab == null))
            {
                if (GUILayout.Button("インポート", GUILayout.Height(30)))
                {
                    CreateLineRenderers();
                }
            }

            EditorGUILayout.Space(5);

            if (jsonFile == null || lineRendererPrefab == null)
            {
                EditorGUILayout.HelpBox("JSONファイルとラインレンダラープレハブの両方を選択してください。", MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
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
                // JSONデータの解析
                LineRendererData data = JsonUtility.FromJson<LineRendererData>(jsonFile.text);
                var groupedData = new Dictionary<string, List<LineData>>();

                // データを色ごとにグループ化
                foreach (var line in data.exportedData)
                {
                    var colorKey = line.color.value[0];
                    if (!groupedData.ContainsKey(colorKey))
                    {
                        groupedData[colorKey] = new List<LineData>();
                    }
                    groupedData[colorKey].Add(line);
                }

                // 親オブジェクトの作成または取得
                GameObject parent = parentObject;
                if (parent == null)
                {
                    parent = new GameObject($"Imported QvPen ({data.timestamp})");
                    Undo.RegisterCreatedObjectUndo(parent, "親オブジェクトの作成");
                }

                // 各グループに対してLineRendererを作成
                foreach (var group in groupedData)
                {
                    GameObject groupObject = new GameObject($"Group_{group.Key}");
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

                        // lineObjのTransformを設定
                        lineObj.transform.position = center;
                        lineObj.transform.parent = groupObject.transform;
                        lineObj.name = $"LineRenderer_{lineData.color.value[0]}";

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

                EditorUtility.DisplayDialog("成功", $"ラインレンダラーを{data.exportedData.Count}個作成しました！", "OK");
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