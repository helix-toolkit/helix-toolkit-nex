// Draw types must match the enum values in DrawStreamVariants
#define DRAW_TYPE_HITABLE (1 << 2)

bool isHitable(uint drawType) {
    return (drawType & DRAW_TYPE_HITABLE) != 0;
}
