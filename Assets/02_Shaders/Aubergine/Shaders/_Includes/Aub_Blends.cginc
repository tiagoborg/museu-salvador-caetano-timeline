// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

#ifndef AUB_BLENDS_INCLUDED
#define AUB_BLENDS_INCLUDED

/**********************INCLUDES**********************/


/**********************STRUCTS**********************/
struct a2f_uv0 {
	float4 vertex : POSITION;
	float4 texcoord : TEXCOORD0;
};

struct v2f_uv0 {
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
};

/**********************VERTS**********************/
v2f_uv0 vert_uv0(a2f_uv0 v) {
	v2f_uv0 o;
	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord.xy;
	return o;
}

/********************FUNCTIONS********************/
float4 Darken (float4 a, float4 b) { return float4(min(a.rgb, b.rgb), 1); }
float4 Multiply (float4 a, float4 b) { return (a * b); }
float4 ColorBurn (float4 a, float4 b) { return (1-(1-a)/b); }
float4 LinearBurn (float4 a, float4 b) { return (a+b-1); }
float4 Lighten (float4 a, float4 b) { return float4(max(a.rgb, b.rgb), 1); }
float4 Screen (float4 a, float4 b) { return (1-(1-a)*(1-b)); }
float4 ColorDodge (float4 a, float4 b) { return (a/(1-b)); }
float4 LinearDodge (float4 a, float4 b) { return (a+b); }

float4 Difference (float4 a, float4 b) { return (abs(a-b)); }
float4 Exclusion (float4 a, float4 b) { return (0.5-2*(a-0.5)*(b-0.5)); }

#endif