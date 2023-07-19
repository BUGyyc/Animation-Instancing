/*
THIS FILE IS PART OF Animation Instancing PROJECT
AnimationInstancing.cs - The core part of the Animation Instancing library

©2017 Jin Xiaoyu. All Rights Reserved.
*/

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

namespace AnimationInstancing
{
    [AddComponentMenu("AnimationInstancing")]
    public class AnimationInstancing : MonoBehaviour
    {
        private Animator animator = null;
        [NonSerialized]
        public Transform worldTransform;
        //public GameObject prefab { get; set; }

        /// <summary>
        /// ！实例化的原型
        /// </summary>
		public GameObject prototype;
        /// <summary>
        /// ！包围盒位置
        /// </summary>
        public BoundingSphere boundingSpere;
        /// <summary>
        /// ! 包围盒是否可见
        /// </summary>
        /// <value></value>
        public bool visible { get; set; }
        /// <summary>
        ///! Attach 需要指定 Parent
        /// </summary>
        /// <value></value>
        public AnimationInstancing parentInstance { get; set; }
        /// <summary>
        /// ！播放速度基数
        /// </summary>
        public float playSpeed = 1.0f;
        /// <summary>
        /// ！阴影投射模式
        /// </summary>
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
        /// <summary>
        /// ！是否接受阴影
        /// </summary>
        public bool receiveShadow;
        [NonSerialized]
        /// <summary>
        /// ！GameLayer 
        /// </summary>
        public int layer;
        float speedParameter = 1.0f, cacheParameter = 1.0f;
        WrapMode wrapMode;
        public WrapMode Mode
        {
             get {return wrapMode;}
             set {wrapMode = value;}
        }
        public bool IsLoop() { return Mode == WrapMode.Loop; }
        public bool IsPause() { return speedParameter == 0.0f; }
        /// <summary>
        /// ! 是否使用 RootMotion
        /// </summary>
        public bool applyRootMotion = false;
        [Range(1, 4)]
        /// <summary>
        /// ！Mesh 采样的骨骼点数
        /// </summary>
        public int bonePerVertex = 4;
        [NonSerialized]
        public float curFrame;
        [NonSerialized]
        public float preAniFrame;
        [NonSerialized]
        public int aniIndex = -1;
        [NonSerialized]
        /// <summary>
        /// ！即将播放的动画索引
        /// </summary>
        public int preAniIndex = -1;
        [NonSerialized]
        public int aniTextureIndex = -1;
        int preAniTextureIndex = -1;
        float transitionDuration = 0.0f;
        /// <summary>
        /// ！标记是否是过度状态
        /// </summary>
        bool isInTransition = false;
        float transitionTimer = 0.0f;
        [NonSerialized]
        public float transitionProgress = 0.0f;
        private int eventIndex = -1;
        /// <summary>
        /// ！实例包含的动画信息
        /// </summary>
        public List<AnimationInfo> aniInfo;
        private ComparerHash comparer;
        private AnimationInfo searchInfo;
        private AnimationEvent aniEvent = null;
        public class LodInfo
        {
            public int lodLevel;
            public SkinnedMeshRenderer[] skinnedMeshRenderer;
            public MeshRenderer[] meshRenderer;
            public MeshFilter[] meshFilter;
            public AnimationInstancingMgr.VertexCache[] vertexCacheList;
            public AnimationInstancingMgr.MaterialBlock[] materialBlockList;
        }
        [NonSerialized]
        /// <summary>
        /// ！保存实例的 Lod 信息
        /// </summary>
        public LodInfo[] lodInfo;
        /// <summary>
        /// ! Lod 的计算频率
        /// </summary>
        private float lodCalculateFrequency = 0.5f;
        /// <summary>
        /// ！ Lod 频率计时器
        /// </summary>
        private float lodFrequencyCount = 0.0f;
        [NonSerialized]
        public int lodLevel;
        /// <summary>
        /// ！相关的 骨骼 Transform
        /// </summary>
        private Transform[] allTransforms;
        private bool isMeshRender = false;
        [NonSerialized]
        /// <summary>
        /// ！记录挂载点
        /// </summary>
        private List<AnimationInstancing> listAttachment;

        void Start()
        {
            if (!AnimationInstancingMgr.Instance.UseInstancing)
            {
                gameObject.SetActive(false);
                return;
            }

            worldTransform = GetComponent<Transform>();
            animator = GetComponent<Animator>();
            //！ 创建一个包围盒
            boundingSpere = new BoundingSphere(new Vector3(0, 0, 0), 1.0f);
            //！ 挂点列表
            listAttachment = new List<AnimationInstancing>();
            layer = gameObject.layer;
            
            //！蒙皮受骨骼影响的骨骼数量，部分机型，不希望太大，提升性能
            switch (QualitySettings.skinWeights)
            {
                case SkinWeights.TwoBones:
                    bonePerVertex = bonePerVertex > 2?2: bonePerVertex;
                    break;
                case SkinWeights.OneBone:
                    bonePerVertex = 1;
                    break;
            }

            UnityEngine.Profiling.Profiler.BeginSample("Calculate lod");
            LODGroup lod = GetComponent<LODGroup>();
            if (lod != null)
            {
                //! 管理 LOD 信息
                lodInfo = new LodInfo[lod.lodCount];
                LOD[] lods = lod.GetLODs();
                for (int i = 0; i != lods.Length; ++i)
                {
                    if (lods[i].renderers == null)
                    {
                        continue;
                    }

                    LodInfo info = new LodInfo();
                    info.lodLevel = i;
                    //！只是初始化了数组，并没有立刻写入数据
                    info.vertexCacheList = new AnimationInstancingMgr.VertexCache[lods[i].renderers.Length];
                    info.materialBlockList = new AnimationInstancingMgr.MaterialBlock[info.vertexCacheList.Length];
                    List<SkinnedMeshRenderer> listSkinnedMeshRenderer = new List<SkinnedMeshRenderer>();
                    List<MeshRenderer> listMeshRenderer = new List<MeshRenderer>();
                    foreach (var render in lods[i].renderers)
                    {
                        //! 对于不同类型的 Renderer 进行记录
                        if (render is SkinnedMeshRenderer)
                            listSkinnedMeshRenderer.Add((SkinnedMeshRenderer)render);
                        if (render is MeshRenderer)
                            listMeshRenderer.Add((MeshRenderer)render);
                    }
                    //! 把 Mesh 数据记录下来
                    info.skinnedMeshRenderer = listSkinnedMeshRenderer.ToArray();
                    info.meshRenderer = listMeshRenderer.ToArray();
                    //todo, to make sure whether the MeshRenderer can be in the LOD.
                    info.meshFilter = null;
                    //!  Disable the GameObject 
                    for (int j = 0; j != lods[i].renderers.Length; ++j)
                    {
                        lods[i].renderers[j].enabled = false;
                    }
                    lodInfo[i] = info;
                }
            }
            else
            {
                //! 没有更多的 LOD 信息，默认只用一个
                lodInfo = new LodInfo[1];
                LodInfo info = new LodInfo();
                info.lodLevel = 0;
                info.skinnedMeshRenderer = GetComponentsInChildren<SkinnedMeshRenderer>();
                info.meshRenderer = GetComponentsInChildren<MeshRenderer>();
                info.meshFilter = GetComponentsInChildren<MeshFilter>();
                info.vertexCacheList = new AnimationInstancingMgr.VertexCache[info.skinnedMeshRenderer.Length + info.meshRenderer.Length];
                info.materialBlockList = new AnimationInstancingMgr.MaterialBlock[info.vertexCacheList.Length];
                lodInfo[0] = info;

                for (int j = 0; j != info.meshRenderer.Length; ++j)
                {
                    info.meshRenderer[j].enabled = false;
                }
                for (int j = 0; j != info.skinnedMeshRenderer.Length; ++j)
                {
                    info.skinnedMeshRenderer[j].enabled = false;
                }
            }
            UnityEngine.Profiling.Profiler.EndSample();

            if (AnimationInstancingMgr.Instance.UseInstancing
                && animator != null)
            {
                //! 关闭原本的 Animator
                animator.enabled = false;
            }
            visible = true;
            //! 计算包围盒
            CalcBoundingSphere();
            //！包围盒 记录到 Mgr 中
            AnimationInstancingMgr.Instance.AddBoundingSphere(this);
            //！实例对象也记录到 Mgr 中
            AnimationInstancingMgr.Instance.AddInstance(gameObject);
        }


        private void OnDestroy()
        {
            if (!AnimationInstancingMgr.IsDestroy())
            {
                //！移除掉这个实例
                AnimationInstancingMgr.Instance.RemoveInstance(this);
            }
            if (parentInstance != null)
            {
                //! 解除关联
                parentInstance.Deattach(this);
                parentInstance = null;
            }
        }

        private void OnEnable()
        {
            playSpeed = 1.0f;
            visible = true;
            if (listAttachment != null)
            {
                for (int i = 0; i != listAttachment.Count; ++i)
                {
                    listAttachment[i].gameObject.SetActive(true);
                }
            }
        }

        private void OnDisable()
        {
            playSpeed = 0.0f;
            visible = false;
            if (listAttachment != null)
            {
                for (int i = 0; i != listAttachment.Count; ++i)
                {
                    listAttachment[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// ！初始化动画
        /// </summary>
        /// <returns></returns>
        public bool InitializeAnimation()
        {
			if(prototype == null) 
			{
				Debug.LogError("The prototype is NULL. Please select the prototype first.");
			}
			Debug.Assert(prototype != null);
			GameObject thisPrefab = prototype;
            isMeshRender = false;
            if (lodInfo[0].skinnedMeshRenderer.Length == 0)
            {
                // This is only a MeshRenderer, it has no animations.
                isMeshRender = true;
				AnimationInstancingMgr.Instance.AddMeshVertex(prototype.name,
                    lodInfo,
                    null,
                    null,
                    bonePerVertex);
                return true;
            }

			AnimationManager.InstanceAnimationInfo info = AnimationManager.Instance.FindAnimationInfo(prototype, this);
            if (info != null)
            {
                aniInfo = info.listAniInfo;
                Prepare(aniInfo, info.extraBoneInfo);
            }
            searchInfo = new AnimationInfo();
            comparer = new ComparerHash();
            return true;
        }

        /// <summary>
        /// ???
        /// </summary>
        /// <param name="infoList"></param>
        /// <param name="extraBoneInfo"></param>
        public void Prepare(List<AnimationInfo> infoList, ExtraBoneInfo extraBoneInfo)
        {
            aniInfo = infoList;
            //extraBoneInfo = extraBoneInfo;
            List<Matrix4x4> bindPose = new List<Matrix4x4>(150);
            //> 得到 BindPose
            //todo: to optimize, MergeBone don't need to call every time
            Transform[] bones = RuntimeHelper.MergeBone(lodInfo[0].skinnedMeshRenderer, bindPose);
            allTransforms = bones;

            if (extraBoneInfo != null)
            {
                List<Transform> list = new List<Transform>();
                list.AddRange(bones);                
                Transform[] transforms = gameObject.GetComponentsInChildren<Transform>();
                for (int i = 0; i != extraBoneInfo.extraBone.Length; ++i)
                {
                    for (int j = 0; j != transforms.Length; ++j)
                    {
                        if (extraBoneInfo.extraBone[i] == transforms[j].name)
                        {
                            list.Add(transforms[j]);
                        }
                    }
                    bindPose.Add(extraBoneInfo.extraBindPose[i]);
                }
                //! 记录下，所有的骨骼 Transform
                allTransforms = list.ToArray();
            }
            
            //! 添加 MeshVertex   HardCore-----------------------------------------
			AnimationInstancingMgr.Instance.AddMeshVertex(prototype.name,
                lodInfo,
                allTransforms,
                bindPose,
                bonePerVertex);

            foreach (var lod in lodInfo)
            {
                foreach (var cache in lod.vertexCacheList)
                {
                    cache.shadowcastingMode = shadowCastingMode;
                    cache.receiveShadow = receiveShadow;
                    cache.layer = layer;
                }
            }

            Destroy(GetComponent<Animator>());
            //Destroy(GetComponentInChildren<SkinnedMeshRenderer>());

            //！播放第一个索引的动画
            PlayAnimation(0);
        }

        /// <summary>
        /// ! 计算得到一个合适的 包围盒
        /// </summary>
        private void CalcBoundingSphere()
        {
            UnityEngine.Profiling.Profiler.BeginSample("CalcBoundingSphere()");
            Bounds bound = new Bounds(new Vector3(0, 0, 0), new Vector3(1, 1, 1));
            LodInfo info = lodInfo[0];
            for (int i = 0; i != info.meshRenderer.Length; ++i)
            {
                MeshRenderer meshRenderer = info.meshRenderer[i];
                bound.Encapsulate(meshRenderer.bounds);
            }
            for (int i = 0; i != info.skinnedMeshRenderer.Length; ++i)
            {
                SkinnedMeshRenderer skinnedMeshRenderer = info.skinnedMeshRenderer[i];
                bound.Encapsulate(skinnedMeshRenderer.bounds);
            }
            float radius = bound.size.x > bound.size.y ? bound.size.x : bound.size.y;
            radius = radius > bound.size.z ? radius : bound.size.z;
            boundingSpere.radius = radius;
            UnityEngine.Profiling.Profiler.EndSample();
        }

        /// <summary>
        /// ！通过名称的方式播放动画
        /// </summary>
        /// <param name="name"></param>
        public void PlayAnimation(string name)
        {
            int hash = name.GetHashCode();
            //！ 将List 内Hash 一样的索引 输出
            int index = FindAnimationInfo(hash);
            PlayAnimation(index);
        }

        /// <summary>
        /// ！ 播放动画
        /// </summary>
        /// <param name="animationIndex"></param>
        public void PlayAnimation(int animationIndex)
        {
            if (aniInfo == null)
            {
                return;
            }
            if (animationIndex == aniIndex && !IsPause())
            {
                //同一个动画，
                return;
            }

            transitionDuration = 0.0f;
            transitionProgress = 1.0f;
            isInTransition = false;
            Debug.Assert(animationIndex < aniInfo.Count);
            //！合理范围内的动画索引
            if (0 <= animationIndex && animationIndex < aniInfo.Count)
            {
                preAniIndex = aniIndex;
                aniIndex = animationIndex;
                //! 往前多采样半帧
                preAniFrame = (float)(int)(curFrame + 0.5f);
                //！帧号归零
                curFrame = 0.0f;
                //？动画事件重置？？
                eventIndex = -1;
                preAniTextureIndex = aniTextureIndex;
                aniTextureIndex = aniInfo[aniIndex].textureIndex;
                wrapMode = aniInfo[aniIndex].wrapMode;
                //！设定一个常规速度
                speedParameter = 1.0f;
            }
            else
            {
                Debug.LogWarning("The requested animation index is out of the count.");
                return;
            }
            RefreshAttachmentAnimation(aniTextureIndex);
        }

        /// <summary>
        /// ！设定过度时间
        /// </summary>
        /// <param name="animationName"></param>
        /// <param name="duration"></param>
        public void CrossFade(string animationName, float duration)
        {
            int hash = animationName.GetHashCode();
            int index = FindAnimationInfo(hash);
            CrossFade(index, duration);
        }

        /// <summary>
        /// ！设定过度时间
        /// </summary>
        /// <param name="animationIndex"></param>
        /// <param name="duration"></param>
        public void CrossFade(int animationIndex, float duration)
        {
            PlayAnimation(animationIndex);
            if (duration > 0.0f)
            {
                isInTransition = true;
                transitionTimer = 0.0f;
                transitionProgress = 0.0f;
            }
            else
            {
                transitionProgress = 1.0f;
            }
            transitionDuration = duration;
        }

        /// <summary>
        /// ！ 暂停
        /// </summary>
        public void Pause()
        {
            //！ 记录暂停前的速度
            cacheParameter = speedParameter;
            speedParameter = 0.0f;
        }

        /// <summary>
        /// ！继续播放
        /// </summary>
        public void Resume()
        {
            //！ 恢复到暂停前的速度
            speedParameter = cacheParameter;
        }

        public void Stop()
        {
            aniIndex = -1;
            preAniIndex = -1;
            eventIndex = -1;
            curFrame = 0.0f;
        }

        public bool IsPlaying()
        {
            return aniIndex >= 0 || isMeshRender;
        }

        public bool IsReady()
        {
            return aniInfo != null;
        }

        /// <summary>
        /// ！获取当前索引状态下的 动画信息
        /// </summary>
        /// <returns></returns>
        public AnimationInfo GetCurrentAnimationInfo()
        {
            if (aniInfo != null && 0 <= aniIndex && aniIndex < aniInfo.Count)
            {
                return aniInfo[aniIndex];
            }
            return null;
        }

        /// <summary>
        /// ！获取即将播放的动画信息
        /// </summary>
        /// <returns></returns>
        public AnimationInfo GetPreAnimationInfo()
        {
            if (aniInfo != null && 0 <= preAniIndex && preAniIndex < aniInfo.Count)
            {
                return aniInfo[preAniIndex];
            }
            return null;
        }

        /// <summary>
        /// ? Tick 动画
        /// </summary>
        public void UpdateAnimation()
        {
            if (aniInfo == null || IsPause())
                return;

            if (isInTransition)
            {
                //! 处于过度动画中
                transitionTimer += Time.deltaTime;
                float weight = transitionTimer / transitionDuration;
                //！计算进度
                transitionProgress = Mathf.Min(weight, 1.0f);
                if (transitionProgress >= 1.0f)
                {
                    //！过度动画结束，标记结束
                    isInTransition = false;
                    preAniIndex = -1;
                    preAniFrame = -1;
                }
            }
            //！得到一个最终速度
            float speed = playSpeed * speedParameter;
            //! 得到 Tick 的帧号
            curFrame += speed * Time.deltaTime * aniInfo[aniIndex].fps;
            int totalFrame = aniInfo[aniIndex].totalFrame;
            switch (wrapMode)
            {
                case WrapMode.Loop:
                {
                    if (curFrame < 0f)
                        curFrame += (totalFrame - 1);
                    else if (curFrame > totalFrame - 1)
                        curFrame -= (totalFrame - 1);
                    break;
                }
                case WrapMode.PingPong:
                {
                    if (curFrame < 0f)
                    {
                        speedParameter = Mathf.Abs(speedParameter);
                        curFrame = Mathf.Abs(curFrame);
                    }
                    else if (curFrame > totalFrame - 1)
                    {
                        speedParameter = -Mathf.Abs(speedParameter);
                        curFrame = 2 * (totalFrame - 1) - curFrame;
                    }
                    break;
                }
                case WrapMode.Default:
                case WrapMode.Once:
                {
                    if (curFrame < 0f || curFrame > totalFrame - 1.0f)
                    {
                        Pause();
                    }
                    break;
                }
            }
            //！纠正最后的帧号取值范围
            curFrame = Mathf.Clamp(curFrame, 0f, totalFrame - 1);
            for (int i = 0; i != listAttachment.Count; ++i)
            {
                //! Update Attach Point
                AnimationInstancing attachment = listAttachment[i];
                attachment.transform.position = transform.position;
                attachment.transform.rotation = transform.rotation;
            }
            //！ 更新动画事件
            UpdateAnimationEvent();
        }

        /// <summary>
        /// ！ 根据相机位置，更新 LOD
        /// </summary>
        /// <param name="cameraPosition"></param>
        public void UpdateLod(Vector3 cameraPosition)
        {
            lodFrequencyCount += Time.deltaTime;
            if (lodFrequencyCount > lodCalculateFrequency)
            {
                float sqrLength = (cameraPosition - worldTransform.position).sqrMagnitude;
                if (sqrLength < 50.0f)
                    lodLevel = 0;
                else if (sqrLength < 500.0f)
                    lodLevel = 1;
                else
                    lodLevel = 2;
                lodFrequencyCount = 0.0f;
                lodLevel = Mathf.Clamp(lodLevel, 0, lodInfo.Length - 1);
            }
        }

        /// <summary>
        /// ！ 更新动画事件
        /// </summary>
        private void UpdateAnimationEvent()
        {
            AnimationInfo info = GetCurrentAnimationInfo();
            if (info == null)
                return;
            if (info.eventList.Count == 0)
                return;

            if (aniEvent == null)
            {
                float time = curFrame / info.fps;
                for (int i = eventIndex >= 0? eventIndex: 0; i < info.eventList.Count; ++i)
                {
                    if (info.eventList[i].time > time)
                    {
                        aniEvent = info.eventList[i];
                        eventIndex = i;
                        break;
                    }
                }
            }

            if (aniEvent != null)
            {
                float time = curFrame / info.fps;
                if (aniEvent.time <= time)
                {
                    gameObject.SendMessage(aniEvent.function, aniEvent);
                    aniEvent = null;
                }
            }
        }

        /// <summary>
        /// ！ 根据Hash，查找动画索引
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        private int FindAnimationInfo(int hash)
        {
            if (aniInfo == null)
                return -1;
            searchInfo.animationNameHash = hash;
            return aniInfo.BinarySearch(searchInfo, comparer);
        }

        /// <summary>
        /// ！通过骨骼名称，管理挂载点，例如：武器节点关联
        /// </summary>
        /// <param name="boneName"></param>
        /// <param name="attachment"></param>
        public void Attach(string boneName, AnimationInstancing attachment)
        {
            int index = -1;
            int hashBone = boneName.GetHashCode();
            for (int i = 0; i != allTransforms.Length; ++i)
            {
                if (allTransforms[i].name.GetHashCode() == hashBone)
                {
                    index = i;
                    break;
                }
            }
            Debug.Assert(index >= 0);
            if (index < 0)
            {
                Debug.LogError("Can't find the bone.");
                return;
            }
            if (attachment.lodInfo[0].meshRenderer.Length == 0 && attachment.lodInfo[0].skinnedMeshRenderer.Length == 0)
            {
                Debug.LogError("The attachment doesn't have a Renderer");
                return;
            }

            //! Attach 需要指定 Parent
            attachment.parentInstance = this;
            AnimationInstancingMgr.VertexCache parentCache = AnimationInstancingMgr.Instance.FindVertexCache(lodInfo[0].skinnedMeshRenderer[0].name.GetHashCode());
            listAttachment.Add(attachment);
            
            int nameCode = boneName.GetHashCode();
            nameCode += attachment.lodInfo[0].meshRenderer.Length > 0? attachment.lodInfo[0].meshRenderer[0].name.GetHashCode(): 0;
            if (attachment.lodInfo[0].meshRenderer.Length == 0)
            {
                //todo, to support the attachment that has skinnedMeshRenderer;
                int skinnedMeshRenderCount = attachment.lodInfo[0].skinnedMeshRenderer.Length;
                nameCode += skinnedMeshRenderCount > 0? attachment.lodInfo[0].skinnedMeshRenderer[0].name.GetHashCode(): 0;
            }
            AnimationInstancingMgr.VertexCache cache = AnimationInstancingMgr.Instance.FindVertexCache(nameCode);

			AnimationInstancingMgr.Instance.AddMeshVertex(attachment.prototype.name,
                        attachment.lodInfo,
                        null,
                        null,
                        attachment.bonePerVertex,
                        boneName);
            // if we can reuse the VertexCache, we don't need to create one
            if (cache != null) 
            {
                cache.boneTextureIndex = parentCache.boneTextureIndex;
                return;
            }

            for (int i = 0; i != attachment.lodInfo.Length; ++i)
            {
                LodInfo info = attachment.lodInfo[i];
                for (int j = 0; j != info.meshRenderer.Length; ++j)
                {
                    cache = info.vertexCacheList[info.skinnedMeshRenderer.Length + j];
                    Debug.Assert(cache != null);
                    if (cache == null)
                    {
                        Debug.LogError("Can't find the VertexCache.");
                        continue;
                    }
                    Debug.Assert(cache.boneTextureIndex < 0 || cache.boneIndex[0].x != index);

                    AnimationInstancingMgr.Instance.BindAttachment(parentCache, cache, info.meshFilter[j].sharedMesh, index);
                    AnimationInstancingMgr.Instance.SetupAdditionalData(cache);
                    cache.boneTextureIndex = parentCache.boneTextureIndex;
                }
            }
        }

        /// <summary>
        /// ！解除关联
        /// </summary>
        /// <param name="attachment"></param>
        public void Deattach(AnimationInstancing attachment)
        {
            attachment.visible = false;
            attachment.parentInstance = null;
            RefreshAttachmentAnimation(-1);
            listAttachment.Remove(attachment);
        }

        /// <summary>
        /// ！获取动画数量
        /// </summary>
        /// <returns></returns>
        public int GetAnimationCount()
        {
            return aniInfo != null? aniInfo.Count: 0;
        }

        /// <summary>
        /// ？？？？刷新挂点的动画
        /// </summary>
        /// <param name="index"></param>
        private void RefreshAttachmentAnimation(int index)
        {
            for (int k = 0; k != listAttachment.Count; ++k)
            {
                AnimationInstancing attachment = listAttachment[k];
                //attachment.aniIndex = aniIndex;
                for (int i = 0; i != attachment.lodInfo.Length; ++i)
                {
                    LodInfo info = attachment.lodInfo[i];
                    for (int j = 0; j != info.meshRenderer.Length; ++j)
                    {
                        //MeshRenderer render = info.meshRenderer[j];
                        AnimationInstancingMgr.VertexCache cache = info.vertexCacheList[info.skinnedMeshRenderer.Length + j];
                        cache.boneTextureIndex = index;
                    }
                }
            }
        }
    }
}