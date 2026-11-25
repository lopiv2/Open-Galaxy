//Adapted FORGE3D force field shader for the Open Galaxy energy shields.
//Slot 0 is reserved for the faint background field, slots 1 - 23 are
//expanding ripples that fade out like a drop hitting water.

Shader "FORGE3D/Force Field"
{
	Properties
	{			
		_FieldTex ("Field Texture" ,2D) = "" {}
		_InnerTint ("Inner Mask Tint", Color) = (1.0, 1.0, 1.0, 1.0)
		_OuterTint ("Outer Mask Tint", Color) = (1.0, 1.0, 1.0, 1.0)		
		_Offset("Mesh Offset", Float) = 0.02	
		_FieldPanSpeed ("Field Texture Pan Speed", Float) = 1.0	
		_FieldBG ("Field Background Visibility", Float) = 1.0	
		_FieldSparks ("Field Sparks Visibility", Float) = 1.0
	}

	Category
	{
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend One One
		AlphaTest Greater .01
		ColorMask RGB
		Cull Off Lighting Off ZWrite Off Fog {Mode Off}

		SubShader
		{	
			Pass
			{

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				#if defined(SHADER_API_D3D9) || defined(SHADER_API_D3D11)
					#pragma target 3.0
				#else
					#pragma glsl
				#endif

				#include "UnityCG.cginc"

				#define INTERPOLATORS 24

				//Impact data: xyz is the local hit point and w is the fade alpha
				float4 _Pos_0;
				float4 _Pos_1;
				float4 _Pos_2;
				float4 _Pos_3;
				float4 _Pos_4;
				float4 _Pos_5;
				float4 _Pos_6;
				float4 _Pos_7;
				float4 _Pos_8;
				float4 _Pos_9;
				float4 _Pos_10;
				float4 _Pos_11;
				float4 _Pos_12;	
				float4 _Pos_13;
				float4 _Pos_14;
				float4 _Pos_15;
				float4 _Pos_16;
				float4 _Pos_17;
				float4 _Pos_18;
				float4 _Pos_19;
				float4 _Pos_20;
				float4 _Pos_21;
				float4 _Pos_22;
				float4 _Pos_23;

				//Ring thickness in object space
				float _Pow_0;
				float _Pow_1;
				float _Pow_2;
				float _Pow_3;
				float _Pow_4;
				float _Pow_5;
				float _Pow_6;
				float _Pow_7;
				float _Pow_8;
				float _Pow_9;
				float _Pow_10;
				float _Pow_11;
				float _Pow_12;
				float _Pow_13;
				float _Pow_14;
				float _Pow_15;
				float _Pow_16;
				float _Pow_17;
				float _Pow_18;
				float _Pow_19;
				float _Pow_20;
				float _Pow_21;
				float _Pow_22;
				float _Pow_23;

				//Ring radius in object space
				float _Ring_0;
				float _Ring_1;
				float _Ring_2;
				float _Ring_3;
				float _Ring_4;
				float _Ring_5;
				float _Ring_6;
				float _Ring_7;
				float _Ring_8;
				float _Ring_9;
				float _Ring_10;
				float _Ring_11;
				float _Ring_12;
				float _Ring_13;
				float _Ring_14;
				float _Ring_15;
				float _Ring_16;
				float _Ring_17;
				float _Ring_18;
				float _Ring_19;
				float _Ring_20;
				float _Ring_21;
				float _Ring_22;
				float _Ring_23;

				sampler2D _FieldTex;
				fixed4 _FieldTex_ST;
			
				fixed4 _InnerTint;
				fixed4 _OuterTint;

				fixed _FieldBG;
				fixed _FieldSparks;				
				fixed _FieldPanSpeed;
				fixed _Offset;				

				struct a2v {					
					float4 vertex : POSITION;					
					float2 texcoord : TEXCOORD0;
					float3 normal : NORMAL;
				};

				struct v2f {
					float4 vertex : SV_POSITION;					
					float2 uv : TEXCOORD0;					
					float4 oPos : TEXCOORD2;
					float3 normal: TEXCOORD3;
				};

				float2 uvPanner(float2 uv, fixed x, fixed y)
				{
					fixed t = _Time;
					return float2(uv.x + x * t, uv.y + y * t);
				}

				/// VERTEX
				v2f vert (a2v v)
				{
					v2f o;

					v.vertex.xyz += v.normal * _Offset;
					o.vertex = UnityObjectToClipPos(v.vertex);					
					o.oPos = v.vertex;
					o.normal = v.normal;
					o.uv = TRANSFORM_TEX ( v.texcoord, _FieldTex );					

					return o;
				}

				/// FRAGMENT
				fixed4 frag (v2f i) : COLOR
				{	
					float4 pos[INTERPOLATORS];	
					float width[INTERPOLATORS];			
					float ring[INTERPOLATORS];

					pos[0] = _Pos_0;
					pos[1] = _Pos_1;
					pos[2] = _Pos_2;
					pos[3] = _Pos_3;
					pos[4] = _Pos_4;
					pos[5] = _Pos_5;
					pos[6] = _Pos_6;
					pos[7] = _Pos_7;
					pos[8] = _Pos_8;
					pos[9] = _Pos_9;
					pos[10] = _Pos_10;
					pos[11] = _Pos_11;
					pos[12] = _Pos_12;
					pos[13] = _Pos_13;
					pos[14] = _Pos_14;
					pos[15] = _Pos_15;
					pos[16] = _Pos_16;
					pos[17] = _Pos_17;
					pos[18] = _Pos_18;
					pos[19] = _Pos_19;
					pos[20] = _Pos_20;
					pos[21] = _Pos_21;
					pos[22] = _Pos_22;
					pos[23] = _Pos_23;

					width[0] = _Pow_0;
					width[1] = _Pow_1;
					width[2] = _Pow_2;
					width[3] = _Pow_3;
					width[4] = _Pow_4;
					width[5] = _Pow_5;
					width[6] = _Pow_6;
					width[7] = _Pow_7;
					width[8] = _Pow_8;
					width[9] = _Pow_9;
					width[10] = _Pow_10;
					width[11] = _Pow_11;
					width[12] = _Pow_12;
					width[13] = _Pow_13;
					width[14] = _Pow_14;
					width[15] = _Pow_15;
					width[16] = _Pow_16;
					width[17] = _Pow_17;
					width[18] = _Pow_18;
					width[19] = _Pow_19;
					width[20] = _Pow_20;
					width[21] = _Pow_21;
					width[22] = _Pow_22;
					width[23] = _Pow_23;

					ring[0] = _Ring_0;
					ring[1] = _Ring_1;
					ring[2] = _Ring_2;
					ring[3] = _Ring_3;
					ring[4] = _Ring_4;
					ring[5] = _Ring_5;
					ring[6] = _Ring_6;
					ring[7] = _Ring_7;
					ring[8] = _Ring_8;
					ring[9] = _Ring_9;
					ring[10] = _Ring_10;
					ring[11] = _Ring_11;
					ring[12] = _Ring_12;
					ring[13] = _Ring_13;
					ring[14] = _Ring_14;
					ring[15] = _Ring_15;
					ring[16] = _Ring_16;
					ring[17] = _Ring_17;
					ring[18] = _Ring_18;
					ring[19] = _Ring_19;
					ring[20] = _Ring_20;
					ring[21] = _Ring_21;
					ring[22] = _Ring_22;
					ring[23] = _Ring_23;

					float background = 0.0;
					float ripple = 0.0;

					for (int x = 0; x < INTERPOLATORS; x++)
					{
						float alpha = pos[x].w;

						if (alpha <= 0.0) continue;

						float dist = distance(pos[x].xyz + i.normal * _Offset, i.oPos.xyz);

						if (x == 0)
						{
							//Slot 0 is the faint background field of the whole shield
							background += alpha;
						}
						else
						{
							//Three concentric rings that expand together like a drop in water:
							//a bright leading ring plus two trailing echoes that fade out
							float w = max(width[x], 0.0001);
							float r0 = ring[x];
							float r1 = r0 * 0.66;
							float r2 = r0 * 0.36;

							float wave0 = saturate(1.0 - abs(dist - r0) / w);
							float wave1 = saturate(1.0 - abs(dist - r1) / w);
							float wave2 = saturate(1.0 - abs(dist - r2) / w);

							ripple += (wave0 * wave0 + wave1 * wave1 * 0.55 + wave2 * wave2 * 0.30) * alpha;
						}
					}

					background = saturate(background);
					ripple = saturate(ripple);

					fixed field_Tex = pow(tex2D(_FieldTex, uvPanner(i.uv, _FieldPanSpeed, _FieldPanSpeed)), 2.2).r;
					fixed field_Tex_inv = pow(tex2D(_FieldTex, uvPanner(i.uv, -_FieldPanSpeed, -_FieldPanSpeed)), 2.2).r;

					fixed3 fieldColor = lerp(_OuterTint, _InnerTint, field_Tex.r).rgb;
					fixed sparksMask = saturate(field_Tex * field_Tex_inv).x;
					fixed3 sparks = saturate(sparksMask * fieldColor) * _FieldSparks;

					fixed3 final = background * _OuterTint.rgb * _OuterTint.a * 10;
					final += field_Tex * fieldColor * background * _FieldBG;
					final += sparks * background * 10;
					final += ripple * _InnerTint.rgb * _InnerTint.a * 10;

					return fixed4(final, 1.0);
				}

				ENDCG 

			}	// PASS
		}	// SUBSHADER
	} 	// CATEGORY
}	// SHADER
