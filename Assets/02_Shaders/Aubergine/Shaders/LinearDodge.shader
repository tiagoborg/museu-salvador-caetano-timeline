Shader "Aubergine/Blends/LinearDodge" {
	Properties {
		_TexA ("TexA", 2D) = "white" {}
		_TexB ("TexB", 2D) = "white" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		Pass {
			Lighting Off Fog { Mode Off }
			CGPROGRAM
			#include "Assets/02_Shaders/Aubergine/Shaders/_Includes/Aub_Blends.cginc"
			#pragma vertex vert_uv0
			#pragma fragment frag

			sampler2D _TexA, _TexB;

			float4 frag( v2f_uv0 i ) : COLOR {
				float4 a = tex2D(_TexA, i.uv);
				float4 b = tex2D(_TexB, i.uv);
				return LinearDodge(a, b);
			}
			ENDCG
		}
	}
}