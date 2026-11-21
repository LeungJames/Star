shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_disabled;

uniform sampler2D cell_data : filter_nearest, repeat_disable;
uniform bool charted = true;
uniform float storm : hint_range(0.0, 1.0) = 0.0;
uniform vec3 ochre : source_color = vec3(0.61, 0.45, 0.28);
uniform vec3 rust : source_color = vec3(0.49, 0.27, 0.17);
uniform vec3 silt : source_color = vec3(0.76, 0.61, 0.40);
uniform vec3 concrete : source_color = vec3(0.31, 0.35, 0.35);
uniform vec3 paint : source_color = vec3(0.86, 0.63, 0.31);
uniform vec3 uncharted : source_color = vec3(0.22, 0.19, 0.18);

varying vec3 world;

float hash(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 s = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), s.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0)), s.x), s.y);
}

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = world.xz;
	vec2 drift = p + vec2(noise(p * 0.41), noise(p * 0.37 + 8.0)) * 1.6;
	float strata = noise(drift * vec2(0.17, 0.31));
	float drifts = noise(drift * vec2(0.76, 1.19));
	float stones = noise(p * 15.0);
	float ripple = sin(p.y * 10.0 - p.x * 3.2 + drifts * 2.0) * (drifts - 0.45);
	vec3 sand = mix(rust, ochre, smoothstep(0.14, 0.72, strata));
	sand = mix(sand, silt, smoothstep(0.42, 0.88, drifts) * 0.44);
	sand *= 0.89 + stones * 0.17 + ripple * 0.075;
	vec3 ground = mix(sand * 0.55, uncharted, 0.45);

	if (charted) {
		// R 表示地表类型，G 表示视野状态，B 表示基地地坪。
		vec3 data = texture(cell_data, UV2).rgb;
		float ridge = step(0.8, data.r);
		float ruin = step(0.3, data.r) * (1.0 - ridge);
		float pad = data.b * (1.0 - ruin);
		vec2 h = abs(UV);
		float hex = max(h.x * 1.154701, h.y + h.x * 0.577350);
		float aa = max(fwidth(hex), 0.004);
		float seam = smoothstep(0.976 - aa, 0.976 + aa, hex);
		ground = sand * (0.96 + COLOR.r * 0.08);

		float bands = sin(world.y * 21.0 + drifts * 2.1 + p.x * 0.35);
		vec3 stone = mix(rust * 0.68, ochre * 0.93, strata) * (0.85 + bands * 0.07 + stones * 0.20);
		ground = mix(ground, stone, ridge * smoothstep(0.13, 0.35, world.y));
		float fragments = smoothstep(0.56, 0.78, noise(p * vec2(4.0, 6.0)));
		vec3 debris = mix(rust * 0.57, concrete * 0.78, fragments);
		ground = mix(ground, debris * (0.90 + drifts * 0.24), ruin * (1.0 - seam) * 0.82);

		float chipped = smoothstep(0.51, 0.77, drifts + stones * 0.13);
		vec3 slab = concrete * (0.84 + stones * 0.17 + drifts * 0.18);
		float inset = 1.0 - smoothstep(0.90 - aa, 0.98 + aa, hex);
		ground = mix(ground, mix(slab, sand, chipped * 0.56), pad * inset);
		float stripe = smoothstep(0.76 - aa, 0.76 + aa, hex) *
			(1.0 - smoothstep(0.81 - aa, 0.81 + aa, hex));
		float dashes = smoothstep(0.31, 0.44, fract((UV.x + UV.y * 0.6) * 5.0));
		ground = mix(ground, paint, stripe * dashes * pad * (0.38 - chipped * 0.33));
		ground *= 1.0 - seam * (0.19 + pad * 0.07);

		float fog = 1.0 - data.g;
		ground = mix(ground, uncharted * (0.84 + drifts * 0.28), pow(fog, 1.15) * 0.92);
	}

	float gust = smoothstep(0.43, 0.79, noise((p + vec2(-TIME * 2.4, TIME * 0.52)) * vec2(0.19, 0.88)));
	ground = mix(ground, silt, gust * storm * 0.22);
	ALBEDO = ground;
	ROUGHNESS = 1.0;
}
