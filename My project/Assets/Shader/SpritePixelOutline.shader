Shader "Custom/SpritePixelOutline"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1) // White outline
        _OutlineWidth ("Outline Width", Range(0, 0.01)) = 0.002
        _PixelSize ("Pixel Size", Float) = 0.01 // Adjust for pixel-perfect outline
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize; // For pixel-perfect sampling
            float4 _OutlineColor;
            float _OutlineWidth;
            float _PixelSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                if (col.a > 0) return col; // Return original sprite color if not transparent

                // Sample neighboring pixels (4 directions for pixel-perfect outline)
                float2 offsets[4] = {
                    float2(-_PixelSize * _MainTex_TexelSize.x, 0),
                    float2(_PixelSize * _MainTex_TexelSize.x, 0),
                    float2(0, -_PixelSize * _MainTex_TexelSize.y),
                    float2(0, _PixelSize * _MainTex_TexelSize.y)
                };

                float outline = 0;
                for (int j = 0; j < 4; j++)
                {
                    outline += tex2D(_MainTex, i.uv + offsets[j]).a;
                }

                // If any neighboring pixel is non-transparent, draw white outline
                if (outline > 0)
                    return _OutlineColor;

                return col; // Transparent if no outline
            }
            ENDCG
        }
    }
}