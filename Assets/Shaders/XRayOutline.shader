Shader "SnowLineVR/XRayOutline"
{
    Properties
    {
        _BaseColor ("Outline Color", Color) = (1, 0.85, 0.25, 0.9)
        _OutlineWidth ("Outline Scale", Range(0, 0.3)) = 0.015
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.12
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

        // ============================================================
        // Pass 1：内部填充
        //   画正面，半透明 → 把物体"染"上一层淡色，本体仍然看得见
        //   用 ZTest LEqual，所以不会盖住前面的东西
        // ============================================================
        Pass
        {
            Name "OutlineFill"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _OutlineWidth;
                float _FillAlpha;
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

            half4 frag(Varyings IN) : SV_Target
            {
                return half4(_BaseColor.rgb, _FillAlpha);
            }
            ENDHLSL
        }

        // ============================================================
        // Pass 2：轮廓边
        //   把顶点整体放大 (1 + 宽度)，只画背面，形成一圈外扩的轮廓。
        //
        //   ⚠️ 用 ZTest Always（穿透显示），原因：
        //      外壳只放大了 ~1.5%，对于厚实的物体，
        //      外壳的背面在【物体内部】，深度上比物体正面更远，
        //      用 ZTest LEqual 会被直接剔除 → 轮廓完全看不见。
        //      用 ZTest Always 就能稳定地画出这一圈边。
        // ============================================================
        Pass
        {
            Name "OutlineEdge"
            Tags { "LightMode" = "UniversalForward" }

            Cull Front
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _OutlineWidth;
                float _FillAlpha;
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

                // 从网格中心整体放大（轮廓副本已摆到包围盒中心）
                float3 posOS = IN.positionOS.xyz * (1.0 + _OutlineWidth);

                OUT.positionCS = TransformObjectToHClip(posOS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
