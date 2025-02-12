﻿// Upgrade NOTE: upgraded instancing buffer 'Props' to new syntax.

#ifndef ANIMATION_INSTANCING_BASE
#define ANIMATION_INSTANCING_BASE

//#pragma target 3.0
//! 被采样的纹理
sampler2D _boneTexture;
//! 通常是 4，表示矩阵的行数
int _boneTextureBlockWidth;
//! 表示骨骼数量
int _boneTextureBlockHeight;
//! 纹理的宽
int _boneTextureWidth;
//! 纹理的高
int _boneTextureHeight;

#if (SHADER_TARGET < 30 || SHADER_API_GLES)
uniform float frameIndex;
uniform float preFrameIndex;
uniform float transitionProgress;
#else
UNITY_INSTANCING_BUFFER_START(Props)
	UNITY_DEFINE_INSTANCED_PROP(float, preFrameIndex)
#define preFrameIndex_arr Props
	UNITY_DEFINE_INSTANCED_PROP(float, frameIndex)
#define frameIndex_arr Props
	UNITY_DEFINE_INSTANCED_PROP(float, transitionProgress)
#define transitionProgress_arr Props
UNITY_INSTANCING_BUFFER_END(Props)
#endif

//! 通过帧号和骨骼索引号，得到矩阵 。 frameIndex 表示帧号， boneIndex 表示骨骼编号
half4x4 loadMatFromTexture(uint frameIndex, uint boneIndex)
{
	//！计算块的数量
	uint blockCount = _boneTextureWidth / _boneTextureBlockWidth;

	//! 计算采样的UV 
	int2 uv;
	
	uv.y = frameIndex / blockCount * _boneTextureBlockHeight;
	uv.x = _boneTextureBlockWidth * (frameIndex - _boneTextureWidth / _boneTextureBlockWidth * uv.y);
	
	//！最多受四个骨骼影响，所以除以4，然后再试骨骼索引
	int matCount = _boneTextureBlockWidth / 4;
	uv.x = uv.x + (boneIndex % matCount) * 4;
	uv.y = uv.y + boneIndex / matCount;

	float2 uvFrame;
	//！转换到0-1
	uvFrame.x = uv.x / (float)_boneTextureWidth;
	uvFrame.y = uv.y / (float)_boneTextureHeight;
	half4 uvf = half4(uvFrame, 0, 0);

	//! 一个像素的偏移
	float offset = 1.0f / (float)_boneTextureWidth;
	
	//! 取下相邻的四个颜色，第四个颜色特别
	half4 c1 = tex2Dlod(_boneTexture, uvf);
	uvf.x = uvf.x + offset;
	half4 c2 = tex2Dlod(_boneTexture, uvf);
	uvf.x = uvf.x + offset;
	half4 c3 = tex2Dlod(_boneTexture, uvf);
	uvf.x = uvf.x + offset;
	//! 第四个颜色，默认
	//half4 c4 = tex2Dlod(_boneTexture, uvf);
	half4 c4 = half4(0, 0, 0, 1);

	//float4x4 m = float4x4(c1, c2, c3, c4);

	half4x4 m;
	m._11_21_31_41 = c1;
	m._12_22_32_42 = c2;
	m._13_23_33_43 = c3;
	m._14_24_34_44 = c4;
	return m;
}

half4 skinning(inout appdata_full v)
{
	//！四个权重，对应的是四个骨骼的权重
	fixed4 w = v.color;
	//！四个通道存的是 骨骼索引编号
	half4 bone = half4(v.texcoord2.x, v.texcoord2.y, v.texcoord2.z, v.texcoord2.w);
#if (SHADER_TARGET < 30 || SHADER_API_GLES)
	float curFrame = frameIndex;
	float preAniFrame = preFrameIndex;
	float progress = transitionProgress;
#else
	float curFrame = UNITY_ACCESS_INSTANCED_PROP(frameIndex_arr, frameIndex);
	float preAniFrame = UNITY_ACCESS_INSTANCED_PROP(preFrameIndex_arr, preFrameIndex);
	float progress = UNITY_ACCESS_INSTANCED_PROP(transitionProgress_arr, transitionProgress);
#endif

	//float curFrame = UNITY_ACCESS_INSTANCED_PROP(frameIndex);
	int preFrame = curFrame;
	int nextFrame = curFrame + 1.0f;

	//！当前帧，四个骨骼叠加的矩阵
	half4x4 localToWorldMatrixPre = loadMatFromTexture(preFrame, bone.x) * w.x;
	localToWorldMatrixPre += loadMatFromTexture(preFrame, bone.y) * max(0, w.y);
	localToWorldMatrixPre += loadMatFromTexture(preFrame, bone.z) * max(0, w.z);
	localToWorldMatrixPre += loadMatFromTexture(preFrame, bone.w) * max(0, w.w);

	//! 未来帧，四个骨骼叠加的矩阵
	half4x4 localToWorldMatrixNext = loadMatFromTexture(nextFrame, bone.x) * w.x;
	localToWorldMatrixNext += loadMatFromTexture(nextFrame, bone.y) * max(0, w.y);
	localToWorldMatrixNext += loadMatFromTexture(nextFrame, bone.z) * max(0, w.z);
	localToWorldMatrixNext += loadMatFromTexture(nextFrame, bone.w) * max(0, w.w);
	
	//! Mesh 顶点乘以矩阵，得到坐标
	half4 localPosPre = mul(v.vertex, localToWorldMatrixPre);
	half4 localPosNext = mul(v.vertex, localToWorldMatrixNext);
	//! 取插值
	half4 localPos = lerp(localPosPre, localPosNext, curFrame - preFrame);

	//! 调整法线结果
	half3 localNormPre = mul(v.normal.xyz, (float3x3)localToWorldMatrixPre);
	half3 localNormNext = mul(v.normal.xyz, (float3x3)localToWorldMatrixNext);
	v.normal = normalize(lerp(localNormPre, localNormNext, curFrame - preFrame));
	//！切线结果
	half3 localTanPre = mul(v.tangent.xyz, (float3x3)localToWorldMatrixPre);
	half3 localTanNext = mul(v.tangent.xyz, (float3x3)localToWorldMatrixNext);
	v.tangent.xyz = normalize(lerp(localTanPre, localTanNext, curFrame - preFrame));

	//???
	half4x4 localToWorldMatrixPreAni = loadMatFromTexture(preAniFrame, bone.x);
	half4 localPosPreAni = mul(v.vertex, localToWorldMatrixPreAni);
	//! 最后得到插值结果
	localPos = lerp(localPos, localPosPreAni, (1.0f - progress) * (preAniFrame > 0.0f));
	return localPos;
}

//! 计算带阴影投射的 GPU-Animation
half4 skinningShadow(inout appdata_full v)
{
	half4 bone = half4(v.texcoord2.x, v.texcoord2.y, v.texcoord2.z, v.texcoord2.w);
#if (SHADER_TARGET < 30 || SHADER_API_GLES)
	float curFrame = frameIndex;
	float preAniFrame = preFrameIndex;
	float progress = transitionProgress;
#else
	float curFrame = UNITY_ACCESS_INSTANCED_PROP(frameIndex_arr, frameIndex);
	float preAniFrame = UNITY_ACCESS_INSTANCED_PROP(preFrameIndex_arr, preFrameIndex);
	float progress = UNITY_ACCESS_INSTANCED_PROP(transitionProgress_arr, transitionProgress);
#endif
	int preFrame = curFrame;
	int nextFrame = curFrame + 1.0f;
	//! 这一帧的矩阵
	half4x4 localToWorldMatrixPre = loadMatFromTexture(preFrame, bone.x);
	//! 下一帧的矩阵
	half4x4 localToWorldMatrixNext = loadMatFromTexture(nextFrame, bone.x);
	half4 localPosPre = mul(v.vertex, localToWorldMatrixPre);
	half4 localPosNext = mul(v.vertex, localToWorldMatrixNext);
	//！ 线性插值得到一个坐标
	half4 localPos = lerp(localPosPre, localPosNext, curFrame - preFrame);
	half4x4 localToWorldMatrixPreAni = loadMatFromTexture(preAniFrame, bone.x);
	half4 localPosPreAni = mul(v.vertex, localToWorldMatrixPreAni);
	localPos = lerp(localPos, localPosPreAni, (1.0f - progress) * (preAniFrame > 0.0f));
	//half4 localPos = v.vertex;
	return localPos;
}

//! 顶点着色器阶段
void vert(inout appdata_full v)
{
#ifdef UNITY_PASS_SHADOWCASTER
	v.vertex = skinningShadow(v);
#else
	v.vertex = skinning(v);
#endif
}

//#define DECLARE_VERTEX_SKINNING \
//	#pragma vertex vert 

#endif