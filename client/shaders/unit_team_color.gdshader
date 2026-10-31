shader_type spatial;
render_mode specular_schlick_ggx, cull_back;

// Per-instance uniforms so all 48 articulated Low-Poly units share a single ShaderMaterial
instance uniform vec4 team_color : source_color = vec4(1.0, 0.72, 0.0, 1.0);
instance uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;
instance uniform float knocked_dim : hint_range(0.0, 1.0) = 0.0;

uniform float roughness_val : hint_range(0.0, 1.0) = 0.52;
uniform float metallic_val : hint_range(0.0, 1.0) = 0.15;
uniform float rim_strength : hint_range(0.0, 2.0) = 0.55;

void fragment() {
    // Vertex COLOR.a controls team_color tint weight (1.0 = team jersey/trim, 0.0 = fixed vertex color like skin/vest/boots)
    vec3 tinted_rgb = mix(COLOR.rgb, COLOR.rgb * team_color.rgb, COLOR.a);
    vec3 base_rgb = mix(tinted_rgb, vec3(0.38, 0.11, 0.11), knocked_dim * 0.65);
    vec3 flash_rgb = mix(base_rgb, vec3(1.0, 1.0, 1.0), hit_flash);

    float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 2.6);
    ALBEDO = flash_rgb;
    ROUGHNESS = roughness_val;
    METALLIC = metallic_val;
    EMISSION = team_color.rgb * fresnel * rim_strength + vec3(1.0, 0.92, 0.82) * hit_flash * 1.45;
}
