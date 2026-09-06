Shader "MiniGTA/OceanWater"
{
    // Mobile-first water: procedural Gerstner-ish waves in the vertex stage, depth-based
    // shoreline blending, fresnel rim and a cheap analytic sun glint. No textures at all,
    // which keeps bandwidth (the real mobile GPU bottleneck) at zero for this surface.
    //
    // The wave function here is duplicated on the CPU in WaterVolume.SurfaceHeightAt so
    // swimming lines up with the visible crests. Change one, change the other.

    Properties
    {
        _ShallowColor ("Shallow Color", Color) = (0.22, 0.62, 0.68, 1)
        _DeepColor    ("Deep Color",    Color) = (0.02, 0.13, 0.26, 1)
        _FoamColor    ("Foam Color",    Color) = (0.85, 0.95, 1.0, 1)

        _DepthFade    ("Depth Fade Distance", Float) = 6.0
        _FoamWidth    ("Shoreline Foam Width", Float) = 1.2

        _WaveAmplitude ("Wave Amplitude", Float) = 0.35
        _WaveLength    ("Wave Length",    Float) = 14.0
        _WaveSpeed     ("Wave Speed",     Float) = 0.6

        _Smoothness   ("Smoothness", Range(0,1)) = 0.92
        _FresnelPower ("Fresnel Power", Float) = 5.0
        _Opacity      ("Base Opacity", Range(0,1)) = 0.82
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _FoamColor;
                float  _DepthFade;
                float  _FoamWidth;
                float  _WaveAmplitude;
                float  _WaveLength;
                float  _WaveSpeed;
                float  _Smoothness;
                float  _FresnelPower;
                float  _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float  fogCoord    : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Two crossed sine trains. Cheap, and irregular enough that the tiling does not
            // read as a grid from a low third-person camera.
            float WaveHeight(float3 wpos, float t)
            {
                float k = 6.2831853 / max(0.001, _WaveLength);
                float w1 = sin((wpos.x + wpos.z) * k + t * _WaveSpeed);
                float w2 = sin((wpos.x * 0.7 - wpos.z * 1.3) * k * 0.6 + t * _WaveSpeed * 1.4);
                return (w1 + w2 * 0.5) * _WaveAmplitude;
            }

            // Analytic normal by finite difference: two extra WaveHeight calls beats
            // transforming a normal map, and needs no texture fetch.
            float3 WaveNormal(float3 wpos, float t)
            {
                const float e = 0.35;
                float h  = WaveHeight(wpos, t);
                float hx = WaveHeight(wpos + float3(e, 0, 0), t);
                float hz = WaveHeight(wpos + float3(0, 0, e), t);
                return normalize(float3(h - hx, e, h - hz));
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 wpos = TransformObjectToWorld(IN.positionOS.xyz);
                float t = _Time.y;

                wpos.y += WaveHeight(wpos, t);

                OUT.positionWS = wpos;
                OUT.normalWS   = WaveNormal(wpos, t);
                OUT.positionCS = TransformWorldToHClip(wpos);
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                OUT.fogCoord   = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float2 uvScreen = IN.screenPos.xy / max(IN.screenPos.w, 1e-5);

                // How much water sits between this fragment and the seabed behind it.
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uvScreen), _ZBufferParams);
                float surfaceDepth = IN.screenPos.w;
                float waterColumn = max(0.0, sceneDepth - surfaceDepth);

                float depth01 = saturate(waterColumn / max(0.001, _DepthFade));
                half3 baseCol = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);

                // Foam where the column is thin: this is what makes a beach read as a beach.
                float foam = 1.0 - saturate(waterColumn / max(0.001, _FoamWidth));
                foam = smoothstep(0.35, 1.0, foam);
                baseCol = lerp(baseCol, _FoamColor.rgb, foam * 0.85);

                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                Light sun = GetMainLight();
                float3 L = normalize(sun.direction);
                float3 H = normalize(L + V);

                float ndotl = saturate(dot(N, L));
                half3 diffuse = baseCol * (0.35 + 0.65 * ndotl) * sun.color;

                float gloss = exp2(_Smoothness * 10.0 + 1.0);
                float spec = pow(saturate(dot(N, H)), gloss) * _Smoothness;
                half3 specular = sun.color * spec;

                float fresnel = pow(1.0 - saturate(dot(N, V)), _FresnelPower);
                half3 sky = SampleSH(N);
                half3 color = diffuse + specular + sky * fresnel * 0.6;

                color += _GlossyEnvironmentColor.rgb * fresnel * 0.25;

                // Thin water must fade out or the shoreline shows a hard cut.
                float alpha = saturate(_Opacity + fresnel * 0.3);
                alpha *= saturate(waterColumn / 0.35);
                alpha = max(alpha, foam * 0.9);

                color = MixFog(color, IN.fogCoord);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
