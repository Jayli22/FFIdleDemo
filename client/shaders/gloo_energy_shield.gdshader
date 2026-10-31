shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded;

instance uniform vec4 shield_color : source_color = vec4(0.0, 0.85, 1.0, 0.78);
instance uniform float hp_ratio : hint_range(0.0, 1.0) = 1.0;
uniform vec4 rim_color : source_color = vec4(0.55, 0.96, 1.0, 1.0);

void fragment() {
    float fresnel = pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
    // Procedural hexagonal energy lattice pattern using UV
    vec2 grid_uv = UV * vec2(12.0, 6.0);
    float gx = abs(fract(grid_uv.x) - 0.5);
    float gy = abs(fract(grid_uv.y + floor(grid_uv.x) * 0.5) - 0.5);
    float hex_line = smoothstep(0.38, 0.48, max(gx, gy));

    float pulse = 0.85 + 0.15 * sin(TIME * 4.5 + UV.y * 8.0);
    vec3 col = mix(shield_color.rgb * 0.65, rim_color.rgb, clamp(fresnel * 0.85 + hex_line * 0.55, 0.0, 1.0));
    col = mix(col, COLOR.rgb, 0.25) * pulse * mix(0.65, 1.0, hp_ratio);

    ALBEDO = col;
    ALPHA = clamp(shield_color.a * (0.55 + fresnel * 0.45 + hex_line * 0.30) * mix(0.55, 1.0, hp_ratio), 0.30, 0.92);
}
