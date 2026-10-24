Shader "SnowLineVR/Outline"
{
    Properties
    {
        _BaseColor ("Outline Color", Color) = (1, 0.85, 0.25, 1)
        _OutlineWidth ("Outline Width (pixels)", Range(1, 20)) = 4
        _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ScreenSpaceOutline"
            Tags { "LightMode" = "UniversalForward" }

            // 两面都画（因为要检测"物体外面"的像素）
            Cull Off
            ZWrite Off
            // 关键：用 LEqual，只在物体【前面或同一深度】的像素上工作，
            // 之后在片元里判断"这个像素是不是在物体轮廓之外"
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _OutlineWidth;
                float _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            // 某个屏幕像素的"场景深度"（相机空间线性深度）
            float SceneDepthAt(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // 我这个像素的深度（相机空间，米）
                float myDepth = IN.positionCS.w;

                float2 uv = IN.positionCS.xy * _ScreenParams.zw;   // 1/width, 1/height
                float2 texel = _ScreenParams.zw;

                // ---------- 检查四周，看有没有"物体"存在 ----------
                // 如果【我自己】比周围任何一点都更靠近相机
                //   → 说明我是画在物体【外面】的像素（轮廓）
                // 如果周围有比我更近的点
                //   → 说明我在物体【内部】→ 丢弃
                bool isOutside = true;

                // 采样 8 个方向的边缘像素
                float2 dirs[8] =
                {
                    float2( 1, 0), float2(-1, 0),
                    float2( 0, 1), float2( 0,-1),
                    float2( 1, 1), float2(-1, 1),
                    float2( 1,-1), float2(-1,-1)
                };

                float minAround = 1e9;

                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float2 o = dirs[i] * texel * _OutlineWidth;
                    float d = SceneDepthAt(uv + o);
                    minAround = min(minAround, d);
                }

                // 周围有比我更近的深度 → 我在物体内部 → 丢弃
                isOutside = (myDepth <= minAround + 0.0001);

                if (!isOutside) discard;

                half4 c = _BaseColor;
                c.a *= _Alpha;
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
