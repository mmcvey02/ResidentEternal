// Raccoon City Skylines: draws your Cities: Skylines city into Resident Evil 2's sky, and lays the outbreak over the
// picture. The RaccoonSkylines add-on (RaccoonSkylines.dll) uploads the city's newest frame into RCSK_CITY and sets
// the uniforms marked "set by the add-on".
//
// How the city lines up: Cities: Skylines renders from RE2's camera (same orientation and vertical FOV, re-anchored in
// your city), so a pixel's view ray is the same in both pictures. Wherever RE2 shows sky (its depth is at the far
// plane), the city shows instead: your towers rise above the RPD's walls and Raccoon City's rooftops.
//
// RE2 needs ReShade's depth access: RESHADE_DEPTH_INPUT_IS_REVERSED=1 in the global preprocessor definitions (RE
// Engine uses reversed Z), and the DX11 renderer (Graphics > Rendering mode).
#include "ReShade.fxh"

texture CityTex : RCSK_CITY;
sampler sCity { Texture = CityTex; AddressU = BORDER; AddressV = BORDER; };

// Set by the add-on.
uniform bool CityActive = false;
uniform int CityFlags = 0;                     // bit 2: rows are bottom-up (Unity readback)
uniform float2 CityFov = float2(60.0, 1.7777); // x: city vertical FOV (degrees), y: city aspect
uniform float Infection = 0.0;                 // the anchor district's infection, 0..1
uniform float Darkness = 0.0;                  // 1 during a blackout in the city (eased)
uniform float Daylight = 1.0;                  // the city's clock, 0 night .. 1 noon
uniform float Pulse = 0.0;                     // an outbreak wave just hit, decays to 0
uniform float Timer < source = "timer"; >;

uniform float SkyDepth < ui_type = "drag"; ui_min = 0.9; ui_max = 1.0; ui_step = 0.0001; ui_label = "Sky depth";
	ui_tooltip = "Linearised depth from which RE2's picture counts as sky. Lower it if the city doesn't show; raise it if it covers distant walls."; > = 0.9995;
uniform float SkyFeather < ui_type = "drag"; ui_min = 0.0; ui_max = 0.01; ui_step = 0.0001; ui_label = "Sky edge softness"; > = 0.0004;
uniform float HostFov < ui_type = "drag"; ui_min = 0.0; ui_max = 120.0; ui_step = 0.1; ui_label = "RE2 vertical FOV override";
	ui_tooltip = "0 = trust the FOV the plugin sent to the city. Set it if the city's towers drift as you turn."; > = 0.0;
uniform float CityBlend < ui_type = "drag"; ui_min = 0.0; ui_max = 1.0; ui_step = 0.01; ui_label = "City strength"; > = 1.0;
uniform float GradeMatch < ui_type = "drag"; ui_min = 0.0; ui_max = 1.0; ui_step = 0.01; ui_label = "Match RE2's colour and brightness";
	ui_tooltip = "Pull the city toward the colour and brightness of RE2's own sky (a rainy night turns a sunny city grey)."; > = 0.6;
uniform float HorizonHaze < ui_type = "drag"; ui_min = 0.0; ui_max = 1.0; ui_step = 0.01; ui_label = "Rain haze"; > = 0.35;
uniform float InfectionLook < ui_type = "drag"; ui_min = 0.0; ui_max = 1.0; ui_step = 0.01; ui_label = "Outbreak look";
	ui_tooltip = "How strongly the district's infection tints and vignettes the picture."; > = 0.6;
uniform float BlackoutStrength < ui_type = "drag"; ui_min = 0.0; ui_max = 1.0; ui_step = 0.01; ui_label = "Blackout darkness"; > = 0.6;
uniform int DebugView < ui_type = "combo"; ui_items = "Composite\0Sky mask\0City frame\0"; > = 0;

// RE2's picture at 1/8 size with mips: the top mip is its average colour, the middle ones the local light.
texture HostSmallTex { Width = BUFFER_WIDTH / 8; Height = BUFFER_HEIGHT / 8; Format = RGBA8; MipLevels = 6; };
sampler sHostSmall { Texture = HostSmallTex; AddressU = CLAMP; AddressV = CLAMP; };

float luma(float3 c)
{
	return dot(c, float3(0.2126, 0.7152, 0.0722));
}

float4 PS_HostSmall(float4 pos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	return float4(tex2D(ReShade::BackBuffer, uv).rgb, 1.0);
}

/// Where RE2 pixel uv looks in the city frame (outside [0,1] means the city frame doesn't cover it).
float2 city_uv(float2 uv)
{
	const float d2r = 3.14159265 / 180.0;
	const float hostAspect = BUFFER_WIDTH * BUFFER_RCP_HEIGHT;
	const float tanCity = tan(CityFov.x * d2r * 0.5);
	const float tanHost = HostFov > 0.0 ? tan(HostFov * d2r * 0.5) : tanCity;
	float2 ndc = uv * 2.0 - 1.0;
	ndc.x *= tanHost * hostAspect / (tanCity * CityFov.y);
	ndc.y *= tanHost / tanCity;
	float2 c = ndc * 0.5 + 0.5;
	if ((CityFlags & 2) != 0)
		c.y = 1.0 - c.y;
	return c;
}

float sky_mask(float2 uv)
{
	const float depth = ReShade::GetLinearizedDepth(uv);
	return smoothstep(SkyDepth - SkyFeather, SkyDepth, depth);
}

float4 PS_Composite(float4 pos : SV_Position, float2 uv : TEXCOORD) : SV_Target
{
	float3 col = tex2D(ReShade::BackBuffer, uv).rgb;

	if (CityActive)
	{
		const float2 cuv = city_uv(uv);
		const bool inside = all(cuv >= 0.0) && all(cuv <= 1.0);
		float3 city = tex2D(sCity, cuv).rgb;
		float mask = inside ? sky_mask(uv) * CityBlend : 0.0;

		if (DebugView == 1)
			return float4(mask.xxx, 1.0);
		if (DebugView == 2)
			return float4(inside ? city : float3(1.0, 0.0, 1.0), 1.0);

		// Grade the city toward RE2's sky: its average colour and brightness.
		const float3 hostAvg = tex2Dlod(sHostSmall, float4(0.5, 0.25, 0, 5)).rgb;
		const float3 hostLocal = tex2Dlod(sHostSmall, float4(uv, 0, 3)).rgb;
		const float3 tint = clamp(hostAvg / max(luma(hostAvg), 1e-3), 0.5, 1.8);
		const float bright = clamp(luma(hostLocal) / max(luma(city), 0.05), 0.15, 1.2);
		float3 graded = city * tint * lerp(1.0, bright, saturate(1.0 - Daylight * 0.5));
		city = lerp(city, graded, GradeMatch);
		// Rain haze: fade toward RE2's local sky colour, more near the horizon (lower in the frame).
		city = lerp(city, hostLocal, HorizonHaze * saturate(uv.y * 1.5));
		col = lerp(col, city, mask);
	}

	// The outbreak: a sickly vignette and desaturation that grow with the district's infection.
	const float inf = Infection * InfectionLook;
	const float2 v = uv - 0.5;
	const float vignette = saturate(dot(v, v) * 2.2);
	col = lerp(col, luma(col).xxx, inf * 0.35);
	col = lerp(col, col * float3(0.75, 1.0, 0.7), inf * vignette * 1.5);
	col *= 1.0 - inf * vignette * 0.6;

	// A wave hit: a red flash at the edges.
	col = lerp(col, col * float3(1.6, 0.4, 0.4), Pulse * vignette * 2.0);

	// Blackout: the lights go out in RE2 too, with a flicker as the grid fails.
	const float flicker = 0.9 + 0.1 * sin(Timer * 0.031) * sin(Timer * 0.017);
	col *= 1.0 - Darkness * BlackoutStrength * flicker;

	return float4(saturate(col), 1.0);
}

technique RaccoonSkylines < ui_tooltip = "Your Cities: Skylines city in Resident Evil 2's sky, plus the outbreak look. Needs RaccoonSkylines.dll."; >
{
	pass HostSmall
	{
		VertexShader = PostProcessVS;
		PixelShader = PS_HostSmall;
		RenderTarget = HostSmallTex;
	}
	pass Composite
	{
		VertexShader = PostProcessVS;
		PixelShader = PS_Composite;
	}
}
