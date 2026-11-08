// ------------------------------------------------------------------
// LINE CAP / JOIN SHAPING (shared by vsLine.glsl and psLineTemplate.glsl)
// ------------------------------------------------------------------
// Every segment is still expanded as its own quad, but the vertex shader detects connected
// neighbours (segment s-1 ends where segment s starts, segment s+1 starts where segment s ends,
// with wrap-around for closed loops) and resolves an effective shape for each of the segment's two
// ends. The fragment shader then evaluates a pixel-space signed distance for that shape.
//
// Connected ends are split along the joint's angle bisector: each segment only keeps the fragments
// on its own side, so neighbouring segments never overlap (no double blending at corners) and the
// two halves of a join meet without a gap. The bisector test uses gl_FragCoord, which is identical
// for both segments, so the split is watertight.
//
// Pixel space ("line px"): NDC.xy * 0.5 * screenDimensions — origin at the screen centre, +y up.

// Effective end shapes (packed per end into v_endModes: start | end << 8).
const uint LINE_END_BUTT       = 0u; // flat end at the endpoint (LineCap.Butt)
const uint LINE_END_SQUARE     = 1u; // flat end extended by half the width (LineCap.Square)
const uint LINE_END_ROUND      = 2u; // semicircle centred on the endpoint (LineCap.Round)
const uint LINE_END_MITER_JOIN = 3u; // edges extended to their intersection (LineJoin.Miter)
const uint LINE_END_BEVEL_JOIN = 4u; // outer corners connected by a straight edge (LineJoin.Bevel)
const uint LINE_END_ROUND_JOIN = 5u; // circular arc around the joint (LineJoin.Round)

// LineDraw.lineJoin values.
const uint LINE_JOIN_NONE  = 0u;
const uint LINE_JOIN_MITER = 1u;
const uint LINE_JOIN_BEVEL = 2u;
const uint LINE_JOIN_ROUND = 3u;

// Extra pixels added around the shape so the 1px anti-aliased edge is never clipped by the quad.
const float LINE_AA_PAD = 1.0;

// Joints whose cos(turnAngle / 2) is below this are treated as reversals (near 180° turns): the
// bisector is ill-defined there, so both ends are capped instead of joined.
const float LINE_JOIN_MIN_COS_HALF = 1e-3;

bool isLineJoinMode(uint mode) {
    return mode >= LINE_END_MITER_JOIN;
}
