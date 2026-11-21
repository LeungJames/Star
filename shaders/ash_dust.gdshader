shader_type canvas_item;
render_mode unshaded;

uniform float storm : hint_range(0.0, 1.0) = 0.0;

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(37.719, 79.233))) * 43758.5453);
}

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = smoothstep(0.0, 1.0, fract(p));
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + 1.0), f.x), f.y);
}

void fragment() {
	vec2 flow = vec2(UV.x + UV.y * 0.33, UV.y);
	float clouds = noise(flow * vec2(4.0, 13.0) + vec2(-TIME * 1.3, TIME * 0.18));
	float wisps = noise(flow * vec2(8.0, 47.0) + vec2(-TIME * 4.7, TIME * 0.36));
	float edge = smoothstep(0.10, 0.56, length((UV - 0.5) * vec2(1.0, 0.82)));
	float dust = (smoothstep(0.37, 0.91, clouds) * 0.15 +
		smoothstep(0.61, 0.95, wisps) * 0.13 + edge * 0.08);
	COLOR = vec4(0.83, 0.53, 0.28, dust * storm);
}
