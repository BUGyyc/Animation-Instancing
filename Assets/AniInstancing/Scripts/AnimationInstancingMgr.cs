﻿/*
THIS FILE IS PART OF Animation Instancing PROJECT
AnimationInstancing.cs - The core part of the Animation Instancing library

©2017 Jin Xiaoyu. All Rights Reserved.
*/

using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace AnimationInstancing
{
    [AddComponentMenu("AnimationInstancingMgr")]
    public class AnimationInstancingMgr : Singleton<AnimationInstancingMgr>
    {

        /// <summary>
        /// ? 等同于Mesh的数据，包含Mesh本身和其他一些数据，例如权重信息
        /// </summary>
        public class VertexCache
        {
            public int nameCode;
            public Mesh mesh = null;
            /// <summary>
            /// ???   Key 是通过 材质 生成的HashCode
            /// </summary>
            public Dictionary<int, MaterialBlock> instanceBlockList;

            //?? 引擎中，似乎也最多受四个骨骼影响，所以 Vector4 也够用了
            /// <summary>
            /// ！顶点受影响的权重  
            /// </summary>
            public Vector4[] weight;
            /// <summary>
            /// ！顶点受影响的 骨骼索引
            /// </summary>
            public Vector4[] boneIndex;
            public Material[] materials = null;
            public Matrix4x4[] bindPose;
            public Transform[] bonePose;
            public int boneTextureIndex = -1;

            // these are temporary, should be moved to InstancingPackage
            public ShadowCastingMode shadowcastingMode;
            public bool receiveShadow;
            public int layer;
        }

        /// <summary>
        /// ?? 材质块的数据
        /// </summary>
        public class MaterialBlock
        {
            public InstanceData instanceData;
            public int[] runtimePackageIndex;
            // array[index base on texture][package index]
            public List<InstancingPackage>[] packageList;
        }

        // array[index base on texture][package index][instance index]
        /// <summary>
        /// ？传递给 Shader 的数据
        /// </summary>
        public class InstanceData
        {
            public List<Matrix4x4[]>[] worldMatrix;
            public List<float[]>[] frameIndex;
            public List<float[]>[] preFrameIndex;
            public List<float[]>[] transitionProgress;
        }


        /// <summary>
        /// ！ 单个实例有关的数据
        /// </summary>
        public class InstancingPackage
        {
            public Material[] material;
            public int animationTextureIndex = 0;
            public int subMeshCount = 1;
            public int instancingCount;
            public int size;
            //??
            public MaterialPropertyBlock propertyBlock;
        }


        /// <summary>
        /// ！ 动画Texture 导出数据
        /// </summary>
        public class AnimationTexture
        {
            public string name { get; set; }
            public Texture2D[] boneTexture { get; set; }
            public int blockWidth { get; set; }
            public int blockHeight { get; set; }
        }

        // all object used animation instancing
        /// <summary>
        /// ！ 记录所有的动画实例
        /// </summary>
        List<AnimationInstancing> aniInstancingList;
        // to calculate lod level
        private Transform cameraTransform; 
        /// <summary>
        /// ???
        /// </summary>
        private Dictionary<int, VertexCache> vertexCachePool;
        /// <summary>
        /// ??
        /// </summary>
        private Dictionary<int, InstanceData> instanceDataPool;
        const int InstancingSizePerPackage = 200;
        /// <summary>
        /// ????
        /// </summary>
        int instancingPackageSize = InstancingSizePerPackage;
        public int InstancingPackageSize
        {
            get { return instancingPackageSize; }
            set { instancingPackageSize = value; }
        }
        /// <summary>
        /// ! 记录 动画相关的 Texture
        /// </summary>
        /// <typeparam name="AnimationTexture"></typeparam>
        /// <returns></returns>
        private List<AnimationTexture> animationTextureList = new List<AnimationTexture>();

        [SerializeField]
        private bool useInstancing = true;
        public bool UseInstancing
        {
            get { return useInstancing; }
            set { useInstancing = value; }
        }

        /// <summary>
        ///！ 所有的包围盒
        /// </summary>
        BoundingSphere[] boundingSphere;
        int usedBoundingSphereCount = 0;

        //！注：用上了剔除盒子
        CullingGroup cullingGroup;

        public static AnimationInstancingMgr GetInstance()
        {
            return Singleton<AnimationInstancingMgr>.Instance;
        }

        private void OnEnable()
        {
            boundingSphere = new BoundingSphere[5000];
            InitializeCullingGroup();
            cameraTransform = Camera.main.transform;
            aniInstancingList = new List<AnimationInstancing>(1000);
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2)
            {
                instancingPackageSize = 1;
                UseInstancing = false;
            }

			vertexCachePool = new Dictionary<int, VertexCache>();
			instanceDataPool = new Dictionary<int, InstanceData>();
        }

        private void Start()
        {
            
        }

        /// <summary>
        /// ！ 初始化 CullingGroup
        /// </summary>
        private void InitializeCullingGroup()
        {
            cullingGroup = new CullingGroup();
            cullingGroup.targetCamera = Camera.main;
            cullingGroup.onStateChanged = CullingStateChanged;
            cullingGroup.SetBoundingSpheres(boundingSphere);
            usedBoundingSphereCount = 0;
            cullingGroup.SetBoundingSphereCount(usedBoundingSphereCount);
        }

        void Update()
        {
            //！计算骨骼位置
            ApplyBoneMatrix();
            Render();
        }

        private void Render()
        {
            foreach (var obj in vertexCachePool)
            {
                VertexCache vertexCache = obj.Value;
                foreach (var block in vertexCache.instanceBlockList)
                {
                    List<InstancingPackage>[] packageList = block.Value.packageList;
                    for (int k = 0; k != packageList.Length; ++k)
                    {
                        for (int i = 0; i != packageList[k].Count; ++i)
                        {
                            InstancingPackage package = packageList[k][i];
                            if (package.instancingCount == 0)
                                continue;
                            for (int j = 0; j != package.subMeshCount; ++j)
                            {
                                InstanceData data = block.Value.instanceData; 
                                if (useInstancing)
                                {
#if UNITY_EDITOR
                                    PreparePackageMaterial(package, vertexCache, k);
#endif
                                    package.propertyBlock.SetFloatArray("frameIndex", data.frameIndex[k][i]);
                                    package.propertyBlock.SetFloatArray("preFrameIndex", data.preFrameIndex[k][i]);
                                    package.propertyBlock.SetFloatArray("transitionProgress", data.transitionProgress[k][i]);

                                    //! Hard Core , Draw Mesh Instanced
                                    Graphics.DrawMeshInstanced(vertexCache.mesh,
                                        j,
                                        package.material[j],
                                        data.worldMatrix[k][i],
                                        package.instancingCount,
                                        package.propertyBlock,
                                        vertexCache.shadowcastingMode,
                                        vertexCache.receiveShadow,
                                        vertexCache.layer);
                                }
                                else
                                {
                                    package.material[j].SetFloat("frameIndex", data.frameIndex[k][i][0]);
                                    package.material[j].SetFloat("preFrameIndex", data.preFrameIndex[k][i][0]);
                                    package.material[j].SetFloat("transitionProgress", data.transitionProgress[k][i][0]);
                                    Graphics.DrawMesh(vertexCache.mesh,
                                        data.worldMatrix[k][i][0],
                                        package.material[j],
                                        0,
                                        null,
                                        j);
                                }
                            }
                            package.instancingCount = 0;
                        }
                        block.Value.runtimePackageIndex[k] = 0;
                    }
                }

//                 if (obj.Value.instancingData == null)
//                     continue;
//                 vertexCache.bufInstance.SetData(obj.Value.instancingData);
// 
//                 for (int i = 0; i != vertexCache.subMeshCount; ++i)
//                 {
//                     Material material = vertexCache.instanceMaterial[i];
//                     material.SetBuffer("buf_InstanceMatrices", vertexCache.bufInstance);
//                     vertexCache.args[i][1] = (uint)vertexCache.currentInstancingIndex;
//                     vertexCache.bufArgs[i].SetData(vertexCache.args[i]);
// 
//                     Graphics.DrawMeshInstancedIndirect(vertexCache.mesh,
//                                     i,
//                                     vertexCache.instanceMaterial[i],
//                                     new Bounds(Vector3.zero, new Vector3(10000.0f, 10000.0f, 10000.0f)),
//                                     vertexCache.bufArgs[i]);
//                 }
//                 vertexCache.currentInstancingIndex = 0;
            }
        }

        public void Clear()
        {
            aniInstancingList.Clear();
            cullingGroup.Dispose();
            vertexCachePool.Clear();
            instanceDataPool.Clear();
            InitializeCullingGroup();
        }

        /// <summary>
        /// ！创建一个 实例
        /// </summary>
        /// <param name="prefab"></param>
        /// <returns></returns>
        public GameObject CreateInstance(GameObject prefab)
        {
            Debug.Assert(prefab != null);
            GameObject obj = Instantiate(prefab, Vector3.zero, Quaternion.identity);
            AnimationInstancing script = obj.GetComponent<AnimationInstancing>();
            AnimationInstancing prototypeScript = prefab.GetComponent<AnimationInstancing>();
            script.prototype = prototypeScript.prototype;
            return obj;
        }

        /// <summary>
        /// ！记录一个实例
        /// </summary>
        /// <param name="obj"></param>
        public void AddInstance(GameObject obj)
        {
            AnimationInstancing script = obj.GetComponent<AnimationInstancing>();
            Debug.Assert(script != null);
            if (script == null)
            {
                Debug.LogError("The prefab you created doesn't attach the script 'AnimationInstancing'.");
                Destroy(obj);
                return;
            }

            try
            {
                bool success = script.InitializeAnimation();
                if (success)
                    aniInstancingList.Add(script);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                Debug.Log("Initialize animation failed. Please check out the backed animation infos and regenerate it.");
                script.enabled = false;
            }
        }
        
        

        /// <summary>
        /// ！移除实例
        /// </summary>
        /// <param name="instance"></param>


        /// <summary>
        /// ！移除实例
        /// </summary>
        /// <param name="instance"></param>
        public void RemoveInstance(AnimationInstancing instance)
        {
            Debug.Assert(aniInstancingList != null);
            bool removed = aniInstancingList.Remove(instance);
            if (removed)
            {
                //! bound 数量减小
                --usedBoundingSphereCount;
                //！设定新的数量
                cullingGroup.SetBoundingSphereCount(usedBoundingSphereCount);
                Debug.Assert(usedBoundingSphereCount >= 0);
                if (usedBoundingSphereCount < 0)
                {
                    Debug.DebugBreak();
                }
            }
        }

        void OnDisable()
        {
            ReleaseBuffer();
            cullingGroup.Dispose();
            cullingGroup = null;
        }

#if !UNITY_ANDROID && !UNITY_IPHONE
        private void OnApplicationFocus(bool focus)
        {
            if (focus)
            {
                RefreshMaterial();
            }
        }
#endif


        /// <summary>
        /// ！ 刷新材质
        /// </summary>
        void RefreshMaterial()
        {
            if (vertexCachePool == null)
                return;

            foreach (var obj in vertexCachePool)
            {
                VertexCache cache = obj.Value;
                foreach (var block in cache.instanceBlockList)
                {
                    for (int j = 0; j != block.Value.packageList.Length; ++j)
                    {
                        for (int k = 0; k != block.Value.packageList[j].Count; ++k)
                        {
                            InstancingPackage package = block.Value.packageList[j][k];
                            PreparePackageMaterial(package, cache, j);
                        }
                    }
                }
                
            }
        }
       
        /// <summary>
        /// ！计算骨骼的矩阵
        /// </summary>
        void ApplyBoneMatrix()
        {
            Vector3 cameraPosition = cameraTransform.position;
            for (int i = 0; i != aniInstancingList.Count; ++i)
            {
                AnimationInstancing instance = aniInstancingList[i];
                if (!instance.IsPlaying())
                    continue;
                //! 这里的 aniIndex 是动画索引
                if (instance.aniIndex < 0 && instance.parentInstance == null)
                    continue;

                //！如果有用 RootMotion，则需要修改 Root 的坐标和朝向
                if (instance.applyRootMotion)
                    ApplyRootMotion(instance);

                instance.UpdateAnimation();
                //！更新一下 包围盒的坐标，因为RootMotion 可能已经改动位置
                instance.boundingSpere.position = instance.transform.position;
                boundingSphere[i] = instance.boundingSpere;
                //！ 不可见，不需要绘制
                if (!instance.visible)
                    continue;
                //! 更新Lod
                instance.UpdateLod(cameraPosition);

                AnimationInstancing.LodInfo lod = instance.lodInfo[instance.lodLevel];
                int aniTextureIndex = -1;
                if (instance.parentInstance != null)
                    aniTextureIndex = instance.parentInstance.aniTextureIndex;
                else
                    aniTextureIndex = instance.aniTextureIndex;

                for (int j = 0; j != lod.vertexCacheList.Length; ++j)
                {
                    VertexCache cache = lod.vertexCacheList[j];
                    MaterialBlock block = lod.materialBlockList[j];
                    Debug.Assert(block != null);
                    int packageIndex = block.runtimePackageIndex[aniTextureIndex];
                    Debug.Assert(packageIndex < block.packageList[aniTextureIndex].Count);
                    InstancingPackage package = block.packageList[aniTextureIndex][packageIndex];
                    if (package.instancingCount + 1 > instancingPackageSize)
                    {
                        ++block.runtimePackageIndex[aniTextureIndex];
                        packageIndex = block.runtimePackageIndex[aniTextureIndex];
                        if (packageIndex >= block.packageList[aniTextureIndex].Count)
                        {
                            InstancingPackage newPackage = CreatePackage(block.instanceData,
                                cache.mesh,
                                cache.materials,
                                aniTextureIndex);
                            block.packageList[aniTextureIndex].Add(newPackage);
                            PreparePackageMaterial(newPackage, cache, aniTextureIndex);
                            newPackage.instancingCount = 1;
                        }
                        block.packageList[aniTextureIndex][packageIndex].instancingCount = 1;
                    }
                    else
                        ++package.instancingCount;

                    {
                        VertexCache vertexCache = cache;
                        InstanceData data = block.instanceData;
                        int index = block.runtimePackageIndex[aniTextureIndex];
                        InstancingPackage pkg = block.packageList[aniTextureIndex][index];
                        int count = pkg.instancingCount - 1;
                        if (count >= 0)
                        {
                            Matrix4x4 worldMat = instance.worldTransform.localToWorldMatrix;
                            Matrix4x4[] arrayMat = data.worldMatrix[aniTextureIndex][index];
                            arrayMat[count].m00 = worldMat.m00;
                            arrayMat[count].m01 = worldMat.m01;
                            arrayMat[count].m02 = worldMat.m02;
                            arrayMat[count].m03 = worldMat.m03;
                            arrayMat[count].m10 = worldMat.m10;
                            arrayMat[count].m11 = worldMat.m11;
                            arrayMat[count].m12 = worldMat.m12;
                            arrayMat[count].m13 = worldMat.m13;
                            arrayMat[count].m20 = worldMat.m20;
                            arrayMat[count].m21 = worldMat.m21;
                            arrayMat[count].m22 = worldMat.m22;
                            arrayMat[count].m23 = worldMat.m23;
                            arrayMat[count].m30 = worldMat.m30;
                            arrayMat[count].m31 = worldMat.m31;
                            arrayMat[count].m32 = worldMat.m32;
                            arrayMat[count].m33 = worldMat.m33;
                            float frameIndex = 0, preFrameIndex = -1, transition = 0f;
                            if (instance.parentInstance != null)
                            {
                                frameIndex = instance.parentInstance.aniInfo[instance.parentInstance.aniIndex].animationIndex + instance.parentInstance.curFrame;
                                if (instance.parentInstance.preAniIndex >= 0)
                                    preFrameIndex = instance.parentInstance.aniInfo[instance.parentInstance.preAniIndex].animationIndex + instance.parentInstance.preAniFrame;
                                transition = instance.parentInstance.transitionProgress;
                            }
                            else
                            {
                                frameIndex = instance.aniInfo[instance.aniIndex].animationIndex + instance.curFrame;
                                if (instance.preAniIndex >= 0)
                                    preFrameIndex = instance.aniInfo[instance.preAniIndex].animationIndex + instance.preAniFrame;
                                transition = instance.transitionProgress;
                            }
                            data.frameIndex[aniTextureIndex][index][count] = frameIndex;
                            data.preFrameIndex[aniTextureIndex][index][count] = preFrameIndex;
                            data.transitionProgress[aniTextureIndex][index][count] = transition;
                        }
                    }
                }
            }
        }

        //! Calculate Root Position And Rotation
        private void ApplyRootMotion(AnimationInstancing instance)
        {
            AnimationInfo info = instance.GetCurrentAnimationInfo();
            if (info == null || !info.rootMotion)
                return;

            int preSampleFrame = (int)instance.curFrame;
            //！ 下一帧
            int nextSampleFrame = (int)(instance.curFrame + 1.0f);
            if (nextSampleFrame >= info.totalFrame)
                return;

            Vector3 preVelocity = info.velocity[preSampleFrame];
            Vector3 nextVelocity = info.velocity[nextSampleFrame];
            Vector3 velocity = Vector3.Lerp(preVelocity, nextVelocity, instance.curFrame - preSampleFrame);
            Vector3 angularVelocity = Vector3.Lerp(info.angularVelocity[preSampleFrame], info.angularVelocity[nextSampleFrame], instance.curFrame - preSampleFrame);

            {

                //! Calculate Frame Position Rotation
                Quaternion localQuaternion = instance.worldTransform.localRotation;
                Quaternion delta = Quaternion.Euler(angularVelocity * Time.deltaTime);
                localQuaternion = localQuaternion * delta;

                Vector3 offset = velocity * Time.deltaTime;
                //! With Rotate offset
                offset = localQuaternion * offset;
                //offset.y = 0.0f;
                Vector3 localPosition = instance.worldTransform.localPosition;
                localPosition += offset;
#if UNITY_5_6_OR_NEWER
                instance.worldTransform.SetPositionAndRotation(localPosition, localQuaternion);
#else
                instance.worldTransform.localPosition = localPosition;
                instance.worldTransform.localRotation = localQuaternion;
#endif
            }
        }

        /// <summary>
        /// ！通过名称，查找 AnimationTexture 数据
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private int FindTexture_internal(string name)
        {
            for (int i = 0; i != animationTextureList.Count; ++i)
            {
                AnimationTexture texture = animationTextureList[i] as AnimationTexture;
                if (texture.name == name)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// ！查找 AnimationTexture
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public AnimationTexture FindTexture(string name)
        {
            int index = FindTexture_internal(name);
            if (index >= 0)
                return animationTextureList[index];
            return null;
        }


        public AnimationTexture FindTexture(int index)
        {
            if (0 <= index && index < animationTextureList.Count)
            {
                return animationTextureList[index];
            }
            return null;
        }


        public VertexCache FindVertexCache(int renderName)
        {
            VertexCache cache = null;
            vertexCachePool.TryGetValue(renderName, out cache);
            return cache;
        }

        /// <summary>
        /// ! 从字节数据从读到 AnimationTexture
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="prefabName"></param>
        private void ReadTexture(BinaryReader reader, string prefabName)
        {

            Debug.LogError("Create  Texture");

            TextureFormat format = TextureFormat.RGBAHalf;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES2)
            {
                //todo
                format = TextureFormat.RGBA32;
            }
            int count = reader.ReadInt32();
            int blockWidth = reader.ReadInt32();
            int blockHeight = reader.ReadInt32();

            //! 等同于，读取一个Animator 相关的动画数据
            AnimationTexture aniTexture = new AnimationTexture();
            aniTexture.boneTexture = new Texture2D[count];
            aniTexture.name = prefabName;
            aniTexture.blockWidth = blockWidth;
            aniTexture.blockHeight = blockHeight;
            //！ 将动画需要的 Texture 记录下来
            animationTextureList.Add(aniTexture);

            //！一张张的 RT 读取
            for (int i = 0; i != count; ++i)
            {
                int textureWidth = reader.ReadInt32();
                int textureHeight = reader.ReadInt32();
                int byteLength = reader.ReadInt32();
                byte[] b = new byte[byteLength];
                b = reader.ReadBytes(byteLength);
                //!  Create Texture By Raw Byte File
                Texture2D texture = new Texture2D(textureWidth, textureHeight, format, false);
                //！ 二进制数据写入 RT
                texture.LoadRawTextureData(b);
                texture.filterMode = FilterMode.Point;
                texture.Apply();
                aniTexture.boneTexture[i] = texture;
            }
        }

        /// <summary>
        /// ！从字节数据中 导入 AnimationTexture
        /// </summary>
        /// <param name="prefabName"></param>
        /// <param name="reader"></param>
        /// <returns></returns>
        public bool ImportAnimationTexture(string prefabName, BinaryReader reader)
        {
            if (FindTexture_internal(prefabName) >= 0)
            {
                return true;
            }

            ReadTexture(reader, prefabName);
            return true;
        }

        /// <summary>
        /// ！清理数据
        /// </summary>
        private void ReleaseBuffer()
        {
            if (vertexCachePool != null)
                vertexCachePool.Clear();
        }


        public InstancingPackage CreatePackage(InstanceData data, Mesh mesh, Material[] originalMaterial, int animationIndex)
        {
            InstancingPackage package = new InstancingPackage();
            //! 多个 SubMesh , 多个 Material
            package.material = new Material[mesh.subMeshCount];
            package.subMeshCount = mesh.subMeshCount;
            package.size = 1;
            for (int i = 0; i != mesh.subMeshCount; ++i)
            {
                package.material[i] = new Material(originalMaterial[i]);
                
                //！ 设定开启GPU-Instance
#if UNITY_5_6_OR_NEWER
                package.material[i].enableInstancing = UseInstancing;
#endif
                if (UseInstancing)
                    package.material[i].EnableKeyword("INSTANCING_ON");
                else
                    package.material[i].DisableKeyword("INSTANCING_ON");

                //！给到材质属性块的对象
                package.propertyBlock = new MaterialPropertyBlock();

                //！开启
                package.material[i].EnableKeyword("USE_CONSTANT_BUFFER");
                //！关闭
                package.material[i].DisableKeyword("USE_COMPUTE_BUFFER");
            }

            //! 初始化了多个集合容器的Size
            Matrix4x4[] mat = new Matrix4x4[instancingPackageSize];
            float[] frameIndex = new float[instancingPackageSize];
            float[] preFrameIndex = new float[instancingPackageSize];
            float[] transitionProgress = new float[instancingPackageSize];
            data.worldMatrix[animationIndex].Add(mat);
            data.frameIndex[animationIndex].Add(frameIndex);
            data.preFrameIndex[animationIndex].Add(preFrameIndex);
            data.transitionProgress[animationIndex].Add(transitionProgress);
            return package;
        }

        /// <summary>
        ///！ 通过材质名称获取HashCode
        /// </summary>
        /// <param name="mat"></param>
        /// <returns></returns>
        int GetIdentify(Material[] mat)
        {
            int hash = 0;
            for (int i = 0; i != mat.Length; ++i)
            {
                hash += mat[i].name.GetHashCode();
            }
            return hash;
        }

        /// <summary>
        /// ! 创建一个 InstanceData
        /// </summary>
        /// <param name="packageCount">AnimationTexture 数量</param>
        /// <returns></returns>
        InstanceData CreateInstanceData(int packageCount)
        {
            InstanceData data = new InstanceData();
            data.worldMatrix = new List<Matrix4x4[]>[packageCount];
            data.frameIndex = new List<float[]>[packageCount];
            data.preFrameIndex = new List<float[]>[packageCount];
            data.transitionProgress = new List<float[]>[packageCount];
            for (int i = 0; i != packageCount; ++i)
            {
                data.worldMatrix[i] = new List<Matrix4x4[]>();
                data.frameIndex[i] = new List<float[]>();
                data.preFrameIndex[i] = new List<float[]>();
                data.transitionProgress[i] = new List<float[]>();
            }   
            return data;    
        }


        // alias is to use for attachment, it should be a bone name
        /// <summary>
        /// ???
        /// </summary>
        /// <param name="prefabName"></param>
        /// <param name="lodInfo"></param>
        /// <param name="bones"></param>
        /// <param name="bindPose"></param>
        /// <param name="bonePerVertex"></param>
        /// <param name="alias"></param>
        public void AddMeshVertex(string prefabName,
            AnimationInstancing.LodInfo[] lodInfo,
            Transform[] bones,
            List<Matrix4x4> bindPose,
            int bonePerVertex,
            string alias = null)
        {
            UnityEngine.Profiling.Profiler.BeginSample("AddMeshVertex()");
            for (int x = 0; x != lodInfo.Length; ++x)
            {
                AnimationInstancing.LodInfo lod = lodInfo[x];
                for (int i = 0; i != lod.skinnedMeshRenderer.Length; ++i)
                {
                    Mesh m = lod.skinnedMeshRenderer[i].sharedMesh;
                    if (m == null)
                        continue;

                    int nameCode = lod.skinnedMeshRenderer[i].name.GetHashCode();
                    //! 通过材质名称 获取 hash
                    int identify = GetIdentify(lod.skinnedMeshRenderer[i].sharedMaterials);
                    VertexCache cache = null;
                    //! 判断池中是否有
                    if (vertexCachePool.TryGetValue(nameCode, out cache))
                    {
                        MaterialBlock block = null;
                        if (!cache.instanceBlockList.TryGetValue(identify, out block))
                        {
                            //! 创建一个 MaterialBlock
                            block = CreateBlock(cache, lod.skinnedMeshRenderer[i].sharedMaterials);
                            //！缓存下来
                            cache.instanceBlockList.Add(identify, block);
                        }
                        lod.vertexCacheList[i] = cache;
                        lod.materialBlockList[i] = block;
                        continue;
                    }

                    //! 创建一个 VertexCache
                    VertexCache vertexCache = CreateVertexCache(prefabName, nameCode, 0, m);
                    vertexCache.bindPose = bindPose.ToArray();
                    //? 材质块
                    MaterialBlock matBlock = CreateBlock(vertexCache, lod.skinnedMeshRenderer[i].sharedMaterials);
                    vertexCache.instanceBlockList.Add(identify, matBlock);
                    //? 组装顶点缓存数据
                    SetupVertexCache(vertexCache, matBlock, lod.skinnedMeshRenderer[i], bones, bonePerVertex);
                    lod.vertexCacheList[i] = vertexCache;
                    lod.materialBlockList[i] = matBlock;
                }

                for (int i = 0, j = lod.skinnedMeshRenderer.Length; i != lod.meshRenderer.Length; ++i, ++j)
                {
                    Mesh m = lod.meshFilter[i].sharedMesh;
                    if (m == null)
                        continue;

                    int renderName = lod.meshRenderer[i].name.GetHashCode();
                    int aliasName = (alias != null ? alias.GetHashCode() : 0);
                    //! 通过材质得到的 Key
                    int identify = GetIdentify(lod.meshRenderer[i].sharedMaterials);
                    VertexCache cache = null;
                    if (vertexCachePool.TryGetValue(renderName + aliasName, out cache))
                    { 
                        MaterialBlock block = null;
                        if (!cache.instanceBlockList.TryGetValue(identify, out block))
                        {
                            block = CreateBlock(cache, lod.meshRenderer[i].sharedMaterials);
                            cache.instanceBlockList.Add(identify, block);
                        }
                        lod.vertexCacheList[j] = cache;
                        lod.materialBlockList[j] = block;
                        continue;
                    }

                    VertexCache vertexCache = CreateVertexCache(prefabName, renderName, aliasName, m);
                    if (bindPose != null)
                        vertexCache.bindPose = bindPose.ToArray();
                    MaterialBlock matBlock = CreateBlock(vertexCache, lod.meshRenderer[i].sharedMaterials);
                    vertexCache.instanceBlockList.Add(identify, matBlock);
                    //！组装数据
                    SetupVertexCache(vertexCache, matBlock, lod.meshRenderer[i], m, bones, bonePerVertex);
                    //! 相当于 累计了 Lod 的等级
                    lod.vertexCacheList[lod.skinnedMeshRenderer.Length + i] = vertexCache;
                    lod.materialBlockList[lod.skinnedMeshRenderer.Length + i] = matBlock;
                }
            }

            UnityEngine.Profiling.Profiler.EndSample();
        }

        /// <summary>
        /// ! 获取 AnimationTexture 的数量
        /// </summary>
        /// <param name="vertexCache"></param>
        /// <returns></returns>
        int GetPackageCount(VertexCache vertexCache)
        {
            int packageCount = 1;
            if (vertexCache.boneTextureIndex >= 0)
            {
                AnimationTexture texture = animationTextureList[vertexCache.boneTextureIndex];
                packageCount = texture.boneTexture.Length;
            }
            return packageCount;
        }

        /// <summary>
        /// ! 创建材质块数据
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="materials"></param>
        /// <returns></returns>
        MaterialBlock CreateBlock(VertexCache cache, Material[] materials)
        {
            MaterialBlock block = new MaterialBlock();
            int packageCount = GetPackageCount(cache);
            block.instanceData = CreateInstanceData(packageCount);                             
            block.packageList = new List<InstancingPackage>[packageCount];
            for (int i = 0; i != block.packageList.Length; ++i)
            {
                //! 这里的 i 关联动画索引
               block.packageList[i] = new List<InstancingPackage>();

               InstancingPackage package = CreatePackage(block.instanceData, 
                    cache.mesh,
                    materials, 
                    i);
                block.packageList[i].Add(package);
                PreparePackageMaterial(package, cache, i);
                package.instancingCount = 1;
            }
            block.runtimePackageIndex = new int[packageCount];
            return block;
        }

        /// <summary>
        /// ！ 创建 VertexCache
        /// </summary>
        /// <param name="prefabName"></param>
        /// <param name="renderName"></param>
        /// <param name="alias"></param>
        /// <param name="mesh"></param>
        /// <returns></returns>
        private VertexCache CreateVertexCache(string prefabName, int renderName, int alias, Mesh mesh)
        {
            VertexCache vertexCache = new VertexCache();
            int cacheName = renderName + alias;
            vertexCachePool[cacheName] = vertexCache;
            vertexCache.nameCode = cacheName;
            vertexCache.mesh = mesh;
            //! 骨骼相关的 Texture
            vertexCache.boneTextureIndex = FindTexture_internal(prefabName);
            //! 初始了顶点受影响的权重数组
            vertexCache.weight = new Vector4[mesh.vertexCount];
            //！初始了顶点受影响的骨骼索引
            vertexCache.boneIndex = new Vector4[mesh.vertexCount];

            //？？ AnimationTexture 的数量
            int packageCount = GetPackageCount(vertexCache);

            InstanceData data = null;
            //! 标记一个实例对象+
            int instanceName = prefabName.GetHashCode() + alias;
            if (!instanceDataPool.TryGetValue(instanceName, out data))
            {
                //! 不包含就创建一个
                data = CreateInstanceData(packageCount);
                //! 放入对象池中管理
                instanceDataPool.Add(instanceName, data);
            }
            //???
            vertexCache.instanceBlockList = new Dictionary<int, MaterialBlock>();
            return vertexCache;
        }

        /// <summary>
        /// ! 组装顶点缓存数据
        /// </summary>
        /// <param name="vertexCache"></param>
        /// <param name="block"></param>
        /// <param name="render"></param>
        /// <param name="boneTransform"></param>
        /// <param name="bonePerVertex"></param>
        private void SetupVertexCache(VertexCache vertexCache,
            MaterialBlock block,
            SkinnedMeshRenderer render,
            Transform[] boneTransform,
            int bonePerVertex)
        {
            int[] boneIndex = null;
            if (render.bones.Length != boneTransform.Length)
            {
                //！长度不等
                if (render.bones.Length == 0)
                {
                    //！长度为0 ， 设定唯一的骨骼
                    boneIndex = new int[1];
                    int hashRenderParentName = render.transform.parent.name.GetHashCode();
                    for (int k = 0; k != boneTransform.Length; ++k)
                    {
                        if (hashRenderParentName == boneTransform[k].name.GetHashCode())
                        {
                            //！ 唯一骨骼
                            boneIndex[0] = k;
                            break;
                        }
                    }
                }
                else
                {
                    boneIndex = new int[render.bones.Length];
                    for (int j = 0; j != render.bones.Length; ++j)
                    {
                        boneIndex[j] = -1;
                        Transform trans = render.bones[j];
                        int hashTransformName = trans.name.GetHashCode();
                        for (int k = 0; k != boneTransform.Length; ++k)
                        {
                            if (hashTransformName == boneTransform[k].name.GetHashCode())
                            {
                                //！记录到骨骼对应的索引
                                boneIndex[j] = k;
                                break;
                            }
                        }
                    }

                    if (boneIndex.Length == 0)
                    {
                        boneIndex = null;
                    }
                }
            }

            UnityEngine.Profiling.Profiler.BeginSample("Copy the vertex data in SetupVertexCache()");
            Mesh m = render.sharedMesh;
            BoneWeight[] boneWeights = m.boneWeights;
            Debug.Assert(boneWeights.Length > 0);
            //>>>  HardCore
            for (int j = 0; j != m.vertexCount; ++j)
            {
                //! 遍历所有顶点，记录信息

                //！记录顶点的权重信息，默认四个骨骼影响，后面会有设定骨骼数量的覆盖
                vertexCache.weight[j].x = boneWeights[j].weight0;
                Debug.Assert(vertexCache.weight[j].x > 0.0f);
                vertexCache.weight[j].y = boneWeights[j].weight1;
                vertexCache.weight[j].z = boneWeights[j].weight2;
                vertexCache.weight[j].w = boneWeights[j].weight3;

                //！设定受影响的骨骼索引
                vertexCache.boneIndex[j].x
                    = boneIndex == null ? boneWeights[j].boneIndex0 : boneIndex[boneWeights[j].boneIndex0];
                vertexCache.boneIndex[j].y
                    = boneIndex == null ? boneWeights[j].boneIndex1 : boneIndex[boneWeights[j].boneIndex1];
                vertexCache.boneIndex[j].z
                    = boneIndex == null ? boneWeights[j].boneIndex2 : boneIndex[boneWeights[j].boneIndex2];
                vertexCache.boneIndex[j].w
                    = boneIndex == null ? boneWeights[j].boneIndex3 : boneIndex[boneWeights[j].boneIndex3];
                Debug.Assert(vertexCache.boneIndex[j].x >= 0);


                if (bonePerVertex == 3)
                {
                    //》如果只受三根骨骼影响
                    float rate = 1.0f / (vertexCache.weight[j].x + vertexCache.weight[j].y + vertexCache.weight[j].z);
                    vertexCache.weight[j].x = vertexCache.weight[j].x * rate;
                    vertexCache.weight[j].y = vertexCache.weight[j].y * rate;
                    vertexCache.weight[j].z = vertexCache.weight[j].z * rate;
                    vertexCache.weight[j].w = -0.1f;
                }
                else if (bonePerVertex == 2)
                {
                    //》只受两根骨骼影响
                    float rate = 1.0f / (vertexCache.weight[j].x + vertexCache.weight[j].y);
                    vertexCache.weight[j].x = vertexCache.weight[j].x * rate;
                    vertexCache.weight[j].y = vertexCache.weight[j].y * rate;
                    vertexCache.weight[j].z = -0.1f;
                    vertexCache.weight[j].w = -0.1f;
                }
                else if (bonePerVertex == 1)
                {
                    //> 只受一根骨骼影响
                    vertexCache.weight[j].x = 1.0f;
                    vertexCache.weight[j].y = -0.1f;
                    vertexCache.weight[j].z = -0.1f;
                    vertexCache.weight[j].w = -0.1f;
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();

            //！ 记录一下材质信息
            if (vertexCache.materials == null)
                vertexCache.materials = render.sharedMaterials;
            // ！把权重数据写入到UV2里面
            SetupAdditionalData(vertexCache);
            for (int i = 0; i != block.packageList.Length; ++i)
            {
                InstancingPackage package = CreatePackage(block.instanceData, vertexCache.mesh, render.sharedMaterials, i);
                block.packageList[i].Add(package);
                //vertexCache.packageList[i].Add(package);
                PreparePackageMaterial(package, vertexCache, i);
            }
        }


        /// <summary>
        /// ！组装数据 ， 针对于 MeshRenderer
        /// </summary>
        /// <param name="vertexCache"></param>
        /// <param name="block"></param>
        /// <param name="render"></param>
        /// <param name="mesh"></param>
        /// <param name="boneTransform"></param>
        /// <param name="bonePerVertex"></param>
        private void SetupVertexCache(VertexCache vertexCache,
            MaterialBlock block,
            MeshRenderer render,
            Mesh mesh,
            Transform[] boneTransform,
            int bonePerVertex)
        {
            int boneIndex = -1;
            if (boneTransform != null)
            {
                for (int k = 0; k != boneTransform.Length; ++k)
                {
                    if (render.transform.parent.name.GetHashCode() == boneTransform[k].name.GetHashCode())
                    {
                        boneIndex = k;
                        break;
                    }
                }
            }
            if (boneIndex >= 0)
            {
                //todo
                BindAttachment(vertexCache, vertexCache, vertexCache.mesh, boneIndex);
            }
            if (vertexCache.materials == null)
                vertexCache.materials = render.sharedMaterials;
            SetupAdditionalData(vertexCache);
            for (int i = 0; i != block.packageList.Length; ++i)
            {
                InstancingPackage package = CreatePackage(block.instanceData, vertexCache.mesh, render.sharedMaterials, i);
                block.packageList[i].Add(package);
                PreparePackageMaterial(package, vertexCache, i);
            }
        }


        /// <summary>
        /// ！把权重数据写入到UV2里面
        /// </summary>
        /// <param name="vertexCache"></param>
        public void SetupAdditionalData(VertexCache vertexCache)
        {
            Color[] colors = new Color[vertexCache.weight.Length];            
            for (int i = 0; i != colors.Length; ++i)
            {
                colors[i].r = vertexCache.weight[i].x;
                colors[i].g = vertexCache.weight[i].y;
                colors[i].b = vertexCache.weight[i].z;
                colors[i].a = vertexCache.weight[i].w;
            }
            //！把四个骨骼的权重也写入，用 mesh.Color 这个数据存储
            vertexCache.mesh.colors = colors;

            List<Vector4> uv2 = new List<Vector4>(vertexCache.boneIndex.Length);
            for (int i = 0; i != vertexCache.boneIndex.Length; ++i)
            {
                uv2.Add(vertexCache.boneIndex[i]);
            }
            //！写入到 UV2 上
            vertexCache.mesh.SetUVs(2, uv2);
            vertexCache.mesh.UploadMeshData(false);
        }

        /// <summary>
        /// ! 给 Material 设定需要的数据
        /// </summary>
        /// <param name="package"></param>
        /// <param name="vertexCache"></param>
        /// <param name="aniTextureIndex"></param>
        public void PreparePackageMaterial(InstancingPackage package, VertexCache vertexCache, int aniTextureIndex)
        {
            if (vertexCache.boneTextureIndex < 0)
                return;
                
            for (int i = 0; i != package.subMeshCount; ++i)
            {
                AnimationTexture texture = animationTextureList[vertexCache.boneTextureIndex];
                package.material[i].SetTexture("_boneTexture", texture.boneTexture[aniTextureIndex]);
                package.material[i].SetInt("_boneTextureWidth", texture.boneTexture[aniTextureIndex].width);
                package.material[i].SetInt("_boneTextureHeight", texture.boneTexture[aniTextureIndex].height);
                package.material[i].SetInt("_boneTextureBlockWidth", texture.blockWidth);
                package.material[i].SetInt("_boneTextureBlockHeight", texture.blockHeight);
            }
        }


        public void AddBoundingSphere(AnimationInstancing instance)
        {
            boundingSphere[usedBoundingSphereCount++] = instance.boundingSpere;
            cullingGroup.SetBoundingSphereCount(usedBoundingSphereCount);
            instance.visible = cullingGroup.IsVisible(usedBoundingSphereCount - 1);
        }


        /// <summary>
        /// ！ 视野变化，需要修改剔除结果
        /// </summary>
        /// <param name="evt"></param>
        private void CullingStateChanged(CullingGroupEvent evt)
        {
            Debug.Assert(evt.index < usedBoundingSphereCount);
            if (evt.hasBecomeVisible)
            {
                Debug.Assert(evt.index < aniInstancingList.Count);
                if (aniInstancingList[evt.index].isActiveAndEnabled)
                {
                    aniInstancingList[evt.index].visible = true;
                }
            }
            if (evt.hasBecomeInvisible)
            {
                Debug.Assert(evt.index < aniInstancingList.Count);
                aniInstancingList[evt.index].visible = false;
            }
        }


        public void BindAttachment(VertexCache parentCache, VertexCache attachmentCache, Mesh sharedMesh, int boneIndex)
        {
            //！ 取下 ParentCache 的逆矩阵
            Matrix4x4 mat = parentCache.bindPose[boneIndex].inverse;
            attachmentCache.mesh = Instantiate(sharedMesh);
            Vector3 offset = mat.GetColumn(3);
            Quaternion q = RuntimeHelper.QuaternionFromMatrix(mat);
            Vector3[] vertices = attachmentCache.mesh.vertices;
            for (int k = 0; k != attachmentCache.mesh.vertexCount; ++k)
            {
                //！重新计算坐标
                vertices[k] = q * vertices[k];
                vertices[k] = vertices[k] + offset;
            }
            attachmentCache.mesh.vertices = vertices;

            for (int j = 0; j != attachmentCache.mesh.vertexCount; ++j)
            {
                //！设定，只受一根骨骼影响
                attachmentCache.weight[j].x = 1.0f;
                attachmentCache.weight[j].y = -0.1f;
                attachmentCache.weight[j].z = -0.1f;
                attachmentCache.weight[j].w = -0.1f;
                attachmentCache.boneIndex[j].x = boneIndex;
            }
        }
    }
}
