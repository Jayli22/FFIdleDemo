shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;

uniform vec4 zone_color : source_color = vec4(0.0, 0.83, 1.0, 0.24);
uniform vec4 pulse_color : source_color = vec4(0.35, 0.95, 1.0, 0.65);

void fragment() {
    // Vertical gradient: bright at bottom (ground contact), fading cleanly toward top
    float bottom_glow = pow(clamp(UV.y, 0.0, 1.0), 2.2);
    float top_fade = smoothstep(0.0, 0.22, UV.y);

    // Animated scanlines
    float scan = 0.5 + 0.5 * sin(UV.y * 42.0 - TIME * 5.0);
    float v_stripe = smoothstep(0.82, 0.98, sin(UV.x * 120.0 + TIME * 1.5));

    vec3 col = mix(zone_color.rgb, pulse_color.rgb, bottom_glow * 0.7 + scan * 0.25 + v_stripe * 0.25);
    float alpha = (zone_color.a * 0.65 + bottom_glow * 0.42 + scan * 0.06) * top_fade;

    ALBEDO = col;
    ALPHA = clamp(alpha, 0.0, 0.75);
}
