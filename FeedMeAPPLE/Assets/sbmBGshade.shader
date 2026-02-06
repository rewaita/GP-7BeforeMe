Shader "Custom/MonochromeGradientSkybox"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (1, 1, 1, 1)
        _BottomColor ("Bottom Color", Color) = (0.2, 0.2, 0.2, 1)
        _CubeScale ("Cube Scale", Float) = 50.0
        _CubeSize ("Cube Size", Float) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Background" "Queue"="Background" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _TopColor;
            float4 _BottomColor;
            float _CubeScale;
            float _CubeSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = v.texcoord;
                return o;
            }

            // ノイズ関数（キューブの配置用）
            float random(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 45.164))) * 43758.5453);
            }

            // 回転行列（X軸）
            float3 rotateX(float3 p, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
            }

            // 回転行列（Y軸）
            float3 rotateY(float3 p, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
            }

            // 回転行列（Z軸）
            float3 rotateZ(float3 p, float angle)
            {
                float c = cos(angle);
                float s = sin(angle);
                return float3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Y 軸でのグラデーション
                float yNorm = (i.worldPos.y + 1.0) * 0.5;
                fixed4 gradColor = lerp(_BottomColor, _TopColor, yNorm);

                // キューブの配置判定
                float3 scaledPos = i.worldPos * _CubeScale;
                float3 gridPos = floor(scaledPos);
                float3 localPos = frac(scaledPos) - 0.5;

                // ランダムに出現するキューブ
                float rand = random(gridPos);
                float cubeMask = 0.0;
                
                if (rand > 0.95) // 5% の確率で出現
                {
                    // ランダムな回転角度を生成
                    float angleX = random(gridPos + float3(1, 0, 0)) * 6.28318; // 0 ~ 2π
                    float angleY = random(gridPos + float3(0, 1, 0)) * 6.28318;
                    float angleZ = random(gridPos + float3(0, 0, 1)) * 6.28318;

                    // ランダムなサイズを生成（-2 ~ +2 の幅）
                    float sizeVariation = (random(gridPos + float3(1, 1, 1)) * 4.0 - 2.0);
                    float cubeSizeRandom = _CubeSize + sizeVariation;

                    // 回転を適用
                    float3 rotatedPos = localPos;
                    rotatedPos = rotateX(rotatedPos, angleX);
                    rotatedPos = rotateY(rotatedPos, angleY);
                    rotatedPos = rotateZ(rotatedPos, angleZ);

                    // 球体を立方体に変更（各軸での最大距離で判定）
                    float3 absPos = abs(rotatedPos);
                    float maxDist = max(max(absPos.x, absPos.y), absPos.z);
                    cubeMask = step(maxDist, cubeSizeRandom);
                }

                // キューブが白くなるようブレンド
                fixed4 finalColor = lerp(gradColor, fixed4(1, 1, 1, 1), cubeMask);
                return finalColor;
            }
            ENDCG
        }
    }
}