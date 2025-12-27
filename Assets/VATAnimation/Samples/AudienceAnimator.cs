using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Yudiz.PickleBall
{
    public class AudienceAnimator : MonoBehaviour
    {
        [Serializable]
        public class AnimationClipData
        {
            public string clipName;
            public int startFrame;
            public int endFrame;
        }

        [Serializable]
        public class AudienceMaterialData
        {
            public List<Material> materials = new List<Material>();
            public AnimationClipData idleAnimation;
            public List<AnimationClipData> celebrationAnimations = new List<AnimationClipData>();
        }

        [Header("Materials Configuration")]
        [SerializeField] private List<AudienceMaterialData> audienceMaterials = new List<AudienceMaterialData>();

        [Header("Animation Settings")]
        [SerializeField] private float celebrationDuration = 3f;
        [SerializeField] private float minDelayBetweenCelebrations = 0.1f;
        [SerializeField] private float maxDelayBetweenCelebrations = 0.5f;
        [SerializeField] private float blendDuration = 0.25f;

        // Shader property IDs
        private static readonly int PrevStartFrame = Shader.PropertyToID("_PreviousStartFrame");
        private static readonly int PrevEndFrame   = Shader.PropertyToID("_PreviousEndFrame");
        private static readonly int CurrStartFrame = Shader.PropertyToID("_StartFrame");
        private static readonly int CurrEndFrame   = Shader.PropertyToID("_EndFrame");
        private static readonly int Blend          = Shader.PropertyToID("_AnimationBlend");

        private Coroutine celebrationCoroutine;
        private bool isInitialized;

        private void Start()
        {
            Initialize();
        }

        private void Update() 
        {
            if(Input.GetKeyDown(KeyCode.Space))    
            {
                PlayRandomCelebrations();
            }
        }

        private void Initialize()
        {
            if (isInitialized) return;

            foreach (var data in audienceMaterials)
            {
                if (data.idleAnimation == null) continue;

                foreach (var mat in data.materials)
                {
                    if (mat == null) continue;

                    mat.SetFloat(PrevStartFrame, data.idleAnimation.startFrame);
                    mat.SetFloat(PrevEndFrame,   data.idleAnimation.endFrame);
                    mat.SetFloat(CurrStartFrame, data.idleAnimation.startFrame);
                    mat.SetFloat(CurrEndFrame,   data.idleAnimation.endFrame);
                    mat.SetFloat(Blend, 1f);
                }
            }

            isInitialized = true;
        }

        public void PlayRandomCelebrations()
        {
            if (celebrationCoroutine != null)
                StopCoroutine(celebrationCoroutine);

            celebrationCoroutine = StartCoroutine(PlayCelebrationsRoutine());
        }

        private IEnumerator PlayCelebrationsRoutine()
        {
            foreach (var group in audienceMaterials)
            {
                if (group.materials.Count == 0 || group.celebrationAnimations.Count == 0)
                    continue;

                // var clip = group.celebrationAnimations[UnityEngine.Random.Range(0, group.celebrationAnimations.Count)];

                foreach (var mat in group.materials)
                {
                    if (mat != null)
                    {
                var clip = group.celebrationAnimations[UnityEngine.Random.Range(0, group.celebrationAnimations.Count)];

                        StartCoroutine(CrossFade(mat, clip));
                    }
                }

                yield return new WaitForSeconds(UnityEngine.Random.Range(minDelayBetweenCelebrations, maxDelayBetweenCelebrations));
            }

            yield return new WaitForSeconds(celebrationDuration);

            yield return StartCoroutine(ReturnToIdle());

            celebrationCoroutine = null;
        }

        private IEnumerator ReturnToIdle()
        {
            foreach (var group in audienceMaterials)
            {
                if (group.idleAnimation == null) continue;

                foreach (var mat in group.materials)
                {
                    if (mat != null)
                        StartCoroutine(CrossFade(mat, group.idleAnimation));

                    yield return new WaitForSeconds(UnityEngine.Random.Range(minDelayBetweenCelebrations, maxDelayBetweenCelebrations));
                }
            }
        }

        private IEnumerator CrossFade(Material mat, AnimationClipData nextClip)
        {
            float prevStart = mat.GetFloat(CurrStartFrame);
            float prevEnd   = mat.GetFloat(CurrEndFrame);

            mat.SetFloat(PrevStartFrame, prevStart);
            mat.SetFloat(PrevEndFrame,   prevEnd);
            mat.SetFloat(CurrStartFrame, nextClip.startFrame);
            mat.SetFloat(CurrEndFrame,   nextClip.endFrame);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / blendDuration;
                mat.SetFloat(Blend, Mathf.Clamp01(t));
                yield return null;
            }

            mat.SetFloat(Blend, 1f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            foreach (var data in audienceMaterials)
            {
                if (data.materials == null) data.materials = new List<Material>();
                if (data.celebrationAnimations == null) data.celebrationAnimations = new List<AnimationClipData>();
            }
        }
#endif
    }
}
