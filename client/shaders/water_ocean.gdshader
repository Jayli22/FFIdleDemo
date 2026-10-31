shader_type spatial;
render_mode blend_mix, depth_draw_opaque, specular_schlick_ggx, cull_back;

uniform vec4 deep_water : source_color = vec4(0.055, 0.125, 0.215, 0.94);
uniform vec4 shallow_water : source_color = vec4(0.09, 0.24, 0.36, 0.90);

void vertex() {
    float wave = sin(VERTEX.x * 0.045 + TIME * 1.6) * cos(VERTEX.z * 0.045 + TIME * 1.3) * 0.35;
    VERTEX.y += wave;
}

void fragment() {
    float wave_Highlight = 0.5 + 0.5 * sin(UV.x * 80.0 + TIME * 1.8) * cos(UV.y * 80.0 - TIME * 1.4);
    vec3 col = mix(deep_water.rgb, shallow_water.rgb, wave_Highlight * 0.45);
    ALBEDO = col;
    ROUGHNESS = 0.22;
    METALLIC = 0.18;
    ALPHA = deep_water.a;
}
