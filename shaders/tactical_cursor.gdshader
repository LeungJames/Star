shader_type spatial;
render_mode unshaded, shadows_disabled, depth_draw_never;

uniform vec4 ink : source_color = vec4(0.45, 0.92, 0.84, 1.0);

void fragment() {
	vec2 planar = (UV - 0.5) * 2.0;
	vec2 a = abs(planar);
	float edge = max(a.x * 1.154701, a.y + a.x * 0.577350);
	float aa = max(fwidth(edge), 0.002);
	float border = 1.0 - smoothstep(0.014 - aa, 0.014 + aa, abs(edge - 0.89));
	float fill = 1.0 - smoothstep(0.89 - aa, 0.89 + aa, edge);
	ALBEDO = ink.rgb;
	ALPHA = ink.a * max(border * 0.88, fill * 0.13);
}
