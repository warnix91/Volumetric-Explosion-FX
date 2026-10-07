#ifndef VEFX_VOLUME_DEPTH
#define VEFX_VOLUME_DEPTH
float4 _VefxClipRange;
float4 VefxProxyClip(float4 position)
{
    #if defined(UNITY_REVERSED_Z)
        position.z=max(position.z,0);
    #else
        position.z=min(position.z,position.w);
    #endif
    return position;
}
float2 VefxRayRange(float rayEye,float sceneEye)
{
    float nearEye=_VefxClipRange.y>0?_VefxClipRange.x:_ProjectionParams.y;
    float farEye=_VefxClipRange.y>0?_VefxClipRange.y:_ProjectionParams.z;
    return float2(nearEye,min(sceneEye,farEye))/rayEye;
}
#endif
