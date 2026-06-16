uint64_t getTimeMs();

mat4 getViewProjection();

mat4 getInvViewProjection();

mat4 getView();

mat4 getInvView();

vec3 getCameraPosition();

vec2 getScreenSize();

bool isPointerRingEnabled();

vec3 getPointerRayDirection();

vec3 getPointerRayOrigin();

float getPointerRingOuterDistThreshold();

float getPointerRingInnerDistThreshold();

float getPointerRingColorMix();

vec3 getPointerRingColor();

float getFragToPointerRayDistance();

bool isInPointerRing();

vec4 mixWithPointerRing(in vec4 color);
