using System.Text;
using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QvPenExporter.UdonScript
{
    public class QvPenExporter : UdonSharpBehaviour
    {
        [SerializeField]
        private QvPen_LateSync[] inkPools;

        // ペンの太さのデフォルト値（LineRenderer.widthMultiplierが0の場合に使用）
        [SerializeField, Tooltip("ペンの太さが0の場合に使用するデフォルト値")]
        private float defaultPenWidth = 0.005f;

        private LineRenderer[] lineRenderers;

        public void Export()
        {
            // Ownerを取得
            if (!Networking.IsOwner(Networking.LocalPlayer, gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            // ペンマネージャーが見つからなかった場合
            if (inkPools.Length == 0)
            {
                Debug.LogError("QvPen_PenManager not found.");
                return;
            }
        
        
            Debug.Log($"[QVPEN_EXPORTER] [START_EXPORT]");
            // inkPoolsの中以下の階層のオブジェクト内にある、LineRendererを持つオブジェクトを全て取得
            foreach (QvPen_LateSync inkPool in inkPools)
            {
                // このLateSyncのペン参照から現在の太さを取得
                QvPen_Pen currentPen = inkPool.pen;
                float currentPenWidth = defaultPenWidth; // デフォルト値を初期値に設定
            
                if (currentPen != null)
                {
                    // ペンが存在する場合、PenManagerから太さを取得
                    QvPen_PenManager penManager = currentPen.gameObject.GetComponentInParent<QvPen_PenManager>();
                    if (penManager != null)
                    {
                        currentPenWidth = penManager.inkWidth;
                    }
                }
            
                lineRenderers = inkPool.GetComponentsInChildren<LineRenderer>();

                // LineRendererが見つかった場合
                if (lineRenderers.Length > 0)
                {
                    foreach (LineRenderer lineRenderer in lineRenderers)
                    {
                        // LineRendererの情報を取得
                        Vector3[] positions = new Vector3[lineRenderer.positionCount];
                        lineRenderer.GetPositions(positions);
                    
                        // Colorの情報を取得
                        // もしGradientの場合は、"color": {"type": "gradient", "value" : ["#000000", "#FFFFFF", ...]} という形式にする
                        // それ以外の場合は、"color": {"type": "const", "value" : ["#000000"]} という形式にする
                        Gradient gradient = lineRenderer.colorGradient;
                        Color color = lineRenderer.startColor;
                        // colorのjsonを生成
                        string colorJson = "";
                        if (gradient != null)
                        {
                            colorJson = "\"color\": {\"type\": \"gradient\", \"value\": [";
                            for (int i = 0; i < gradient.colorKeys.Length; i++)
                            {
                                colorJson += "\"" + ColorToHex(gradient.colorKeys[i].color) + "\",";
                            }
                            colorJson = colorJson.TrimEnd(',') + "]}";
                        }
                        else
                        {
                            colorJson = "\"color\": {\"type\": \"const\", \"value\": [\"" + ColorToHex(color) + "\"]}";
                        }
                    
                        // ペンの太さ情報を取得
                        float width = lineRenderer.widthMultiplier;
                    
                        // widthが0の場合はデフォルト値を使用
                        if (width <= 0.0001f)
                        {
                            width = currentPenWidth;
                        
                            // それでも0の場合はデフォルト値を使用
                            if (width <= 0.0001f)
                            {
                                width = defaultPenWidth;
                            }
                        }
                    
                        string widthJson = "\"width\": " + width.ToString();
                    
                        // positionsを文字列に変換
                        StringBuilder positionsString = new StringBuilder();
                        foreach (Vector3 position in positions)
                        {
                            positionsString.Append(position.x).Append(",").Append(position.y).Append(",").Append(position.z).Append(",");
                        }
                    
                        // JSON文字列を生成
                        string jsonString = "{" + colorJson + "," + widthJson + ",\"positions\":[" + positionsString.ToString().TrimEnd(',') + "]}";
                    
                        // JSON文字列をBase64エンコード
                        string base64String = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonString));
                    
                        Debug.Log($"[QVPEN_EXPORTER] [START]{base64String}[END]");
                    }
                
                }
            }
            Debug.Log("[QVPEN_EXPORTER] [END_EXPORT]");
        }

        private string ColorToHex(Color color)
        {
            int r = Mathf.RoundToInt(color.r * 255f);
            int g = Mathf.RoundToInt(color.g * 255f);
            int b = Mathf.RoundToInt(color.b * 255f);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }
    }
}
