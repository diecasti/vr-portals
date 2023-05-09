using RotaryHeart.Lib.SerializableDictionary;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace PortalsVR
{
    public class Portal : MonoBehaviour
    {
        #region Fields
        [SerializeField] private Portal linkedPortal;

        [Header("Settings")]
        [SerializeField] private int recursionLimit = 5;
        [SerializeField] private float nearClipOffset = 0.05f;
        [SerializeField] private float nearClipLimit = 0.2f;
        [SerializeField] private float offsetAmount = 0.044f;
        [SerializeField] private float deformPower = 0.25f;
        [SerializeField] private GameObject[] clippedObjects;

        [Header("Internal References")]
        [SerializeField] private Transform screens;
        [SerializeField] private PortalInfoDictionary portalInfo;

        private Material firstRecursionMat;
        private List<PortalTraveller> trackedTravellers;
        #endregion

        #region Properties
        private int SideOfPortal(Vector3 pos)
        {
            return System.Math.Sign(Vector3.Dot(pos - transform.position, transform.forward));
        }
        private bool SameSideOfPortal(Vector3 posA, Vector3 posB)
        {
            return SideOfPortal(posA) == SideOfPortal(posB);
        }

        public bool IsActive { get; set; } = true;
        #endregion

        #region Methods


        int width, height;

        void Start()
        {
            //estamos haciendo esto porque xr tarda unos frames bastantes genmerosos en inicializar y hasta entonce sno podemos saber la resolucion dwe cada ojo
            StartCoroutine(WidthHeight());
        }

        IEnumerator WidthHeight()
        {
            while (XRSettings.eyeTextureWidth == 0)
                yield return new WaitForEndOfFrame();
            //Debug.Log($"w: {XRSettings.eyeTextureWidth} h: {XRSettings.eyeTextureHeight}");
            width = XRSettings.eyeTextureWidth; //Screen.width;//
            height = XRSettings.eyeTextureHeight; //Screen.height;// 

            foreach (Camera.StereoscopicEye eye in portalInfo.Keys)
            {
                portalInfo[eye].screenMeshFilter = portalInfo[eye].screen.GetComponent<MeshFilter>();
                portalInfo[eye].screen.material.SetInt("displayMask", 1);
                portalInfo[eye].viewTexture = new RenderTexture(XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, 24);

                //Debug.Log(XRSettings.renderViewportScale);
                portalInfo[eye].camera.targetTexture = portalInfo[eye].viewTexture;
                linkedPortal.portalInfo[eye].screen.material.SetTexture("_MainTex", portalInfo[eye].viewTexture);
            }

        }


        private void Awake()
        {
            trackedTravellers = new List<PortalTraveller>();

            //foreach (Camera.StereoscopicEye eye in portalInfo.Keys)
            //{
            //    portalInfo[eye].screenMeshFilter = portalInfo[eye].screen.GetComponent<MeshFilter>();
            //    portalInfo[eye].screen.material.SetInt("displayMask", 1);
            //    portalInfo[eye].viewTexture = new RenderTexture(XRSettings.eyeTextureWidth, XRSettings.eyeTextureHeight, 24);
            //    portalInfo[eye].camera.targetTexture = portalInfo[eye].viewTexture;
            //    linkedPortal.portalInfo[eye].screen.material.SetTexture("_MainTex", portalInfo[eye].viewTexture);
            //}
        }
        private void LateUpdate()
        {
            HandleTravellers();
        }





        private void OnTriggerEnter(Collider other)
        {
            PortalTraveller traveller = other.GetComponent<PortalTraveller>();
            if (traveller && !traveller.InPortal)
            {
                OnTravellerEnterPortal(traveller);
                traveller.InPortal = true;
            }
        }
        private void OnTriggerExit(Collider other)
        {
            PortalTraveller traveller = other.GetComponent<PortalTraveller>();
            if (traveller && trackedTravellers.Contains(traveller))
            {
                traveller.ExitPortalThreshold();
                trackedTravellers.Remove(traveller);
                traveller.InPortal = false;
            }

            portalInfo[Camera.StereoscopicEye.Left].meshDeformer.ClearDeformingForce();
            portalInfo[Camera.StereoscopicEye.Right].meshDeformer.ClearDeformingForce();

            foreach (GameObject clippedObject in clippedObjects)
            {
                clippedObject.SetActive(true);
            }
        }



        private void OnEnable()
        {
            portalInfo[Camera.StereoscopicEye.Left].eye.Portals.Add(this);
            portalInfo[Camera.StereoscopicEye.Right].eye.Portals.Add(this);
        }
        private void OnDisable()
        {
            portalInfo[Camera.StereoscopicEye.Left].eye.Portals.Remove(this);
            portalInfo[Camera.StereoscopicEye.Right].eye.Portals.Remove(this);
        }

        // Called before any portal cameras are rendered for the current frame
        public void PrePortalRender(Camera.StereoscopicEye eye)
        {
            foreach (var traveller in trackedTravellers)
            {
                UpdateSliceParams(traveller,eye);
            }
        }

        public void Render(Camera.StereoscopicEye eye)
        {
            if (!CameraUtility.VisibleFromCamera(linkedPortal.portalInfo[eye].screen, portalInfo[eye].eye.Camera) || !IsActive)
            {
                return;
            }
            //var interlude = portalInfo[eye].alias;
            //interlude.rotati
            var localToWorldMatrix = portalInfo[eye].alias.localToWorldMatrix;
            var renderPositions = new Vector3[recursionLimit];
            var renderRotations = new Quaternion[recursionLimit];

            int startIndex = 0;
            portalInfo[eye].camera.projectionMatrix = portalInfo[eye].eye.Camera.projectionMatrix;
            for (int i = 0; i < recursionLimit; i++)
            {
                if (i > 0)
                {
                    if (!CameraUtility.BoundsOverlap(portalInfo[eye].screenMeshFilter, linkedPortal.portalInfo[eye].screenMeshFilter, portalInfo[eye].camera))
                    {
                        break;
                    }
                }
                localToWorldMatrix = transform.localToWorldMatrix * linkedPortal.transform.worldToLocalMatrix * localToWorldMatrix;
                int renderOrderIndex = recursionLimit - i - 1;
                renderPositions[renderOrderIndex] = localToWorldMatrix.GetColumn(3);
                renderRotations[renderOrderIndex] = localToWorldMatrix.rotation;

                portalInfo[eye].camera.transform.SetPositionAndRotation(renderPositions[renderOrderIndex], renderRotations[renderOrderIndex]);
                startIndex = renderOrderIndex;
            }

            linkedPortal.portalInfo[eye].screen.material.SetInt("displayMask", 0);
            for (int i = startIndex; i < recursionLimit; i++)
            {
                portalInfo[eye].camera.transform.SetPositionAndRotation(renderPositions[i], renderRotations[i]);

                SetNearClipPlane(eye);
                HandleClipping(eye);
                portalInfo[eye].camera.Render();

                if (i == startIndex)
                {
                    linkedPortal.portalInfo[eye].screen.material.SetInt("displayMask", 1);
                }
            }
        }

        float ProtectScreenFromClipping(Vector3 viewPoint, Camera.StereoscopicEye eye)
        {
            float halfHeight = portalInfo[eye].eye.Camera.nearClipPlane * Mathf.Tan(portalInfo[eye].eye.Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfWidth = halfHeight * portalInfo[eye].eye.Camera.aspect;
            float dstToNearClipPlaneCorner = new Vector3(halfWidth, halfHeight, portalInfo[eye].eye.Camera.nearClipPlane).magnitude;
            float screenThickness = dstToNearClipPlaneCorner;

            //Transform screenT = screens.GetChild(eye == Camera.StereoscopicEye.Left ? 0 : 1).transform;
            //bool camFacingSameDirAsPortal = Vector3.Dot(transform.forward, transform.position - viewPoint) > 0;
            //screenT.localScale = new Vector3(screenT.localScale.x, screenT.localScale.y, screenThickness);
            //screenT.localPosition = Vector3.forward * screenThickness * ((camFacingSameDirAsPortal) ? 0.5f : -0.5f);
            return screenThickness;
        }
        void HandleClipping(Camera.StereoscopicEye eye)
        {
            // There are two main graphical issues when slicing travellers
            // 1. Tiny sliver of mesh drawn on backside of portal
            //    Ideally the oblique clip plane would sort this out, but even with 0 offset, tiny sliver still visible
            // 2. Tiny seam between the sliced mesh, and the rest of the model drawn onto the portal screen
            // This function tries to address these issues by modifying the slice parameters when rendering the view from the portal
            // Would be great if this could be fixed more elegantly, but this is the best I can figure out for now
            const float hideDst = -1000;
            const float showDst = 1000;
            float screenThickness = linkedPortal.ProtectScreenFromClipping(portalInfo[eye].camera.transform.position,eye);

            foreach (var traveller in trackedTravellers)
            {
                if (SameSideOfPortal(traveller.transform.position, portalInfo[eye].camera.transform.position))
                {
                    // Addresses issue 1
                    traveller.SetSliceOffsetDst(hideDst, false);
                }
                else
                {
                    // Addresses issue 2
                    traveller.SetSliceOffsetDst(showDst, false);
                }

                // Ensure clone is properly sliced, in case it's visible through this portal:
                int cloneSideOfLinkedPortal = -SideOfPortal(traveller.transform.position);
                bool camSameSideAsClone = linkedPortal.SideOfPortal(portalInfo[eye].camera.transform.position) == cloneSideOfLinkedPortal;
                if (camSameSideAsClone)
                {
                    traveller.SetSliceOffsetDst(screenThickness, true);
                }
                else
                {
                    traveller.SetSliceOffsetDst(-screenThickness, true);
                }
            }

            var offsetFromPortalToCam = portalInfo[eye].camera.transform.position - transform.position;
            foreach (var linkedTraveller in linkedPortal.trackedTravellers)
            {
                var travellerPos = linkedTraveller.graphicsObject.transform.position;
                var clonePos = linkedTraveller.graphicsClone.transform.position;
                // Handle clone of linked portal coming through this portal:
                bool cloneOnSameSideAsCam = linkedPortal.SideOfPortal(travellerPos) != SideOfPortal(portalInfo[eye].camera.transform.position);
                if (cloneOnSameSideAsCam)
                {
                    // Addresses issue 1
                    linkedTraveller.SetSliceOffsetDst(hideDst, true);
                }
                else
                {
                    // Addresses issue 2
                    linkedTraveller.SetSliceOffsetDst(showDst, true);
                }

                // Ensure traveller of linked portal is properly sliced, in case it's visible through this portal:
                bool camSameSideAsTraveller = linkedPortal.SameSideOfPortal(linkedTraveller.transform.position, portalInfo[eye].camera.transform.position);
                if (camSameSideAsTraveller)
                {
                    linkedTraveller.SetSliceOffsetDst(screenThickness, false);
                }
                else
                {
                    linkedTraveller.SetSliceOffsetDst(-screenThickness, false);
                }
            }
        }

        // Called once all portals have been rendered, but before the player camera renders
        public void PostPortalRender(Camera.StereoscopicEye eye)
        {
            foreach (var traveller in trackedTravellers)
            {
                UpdateSliceParams(traveller, eye);
            }
            //ProtectScreenFromClipping(portalInfo[eye].eye.Camera.transform.position, eye);
        }
        private void HandleTravellers()
        {
            if (!IsActive) return;

            for (int i = 0; i < trackedTravellers.Count; i++)
            {
                PortalTraveller traveller = trackedTravellers[i];

                Transform travellerT = traveller.Target;
                var m = linkedPortal.transform.localToWorldMatrix * transform.worldToLocalMatrix * travellerT.localToWorldMatrix;

                Vector3 offsetFromPortal = travellerT.position - transform.position;
                int portalSide = Math.Sign(Vector3.Dot(offsetFromPortal, transform.forward));
                int portalSideOld = Math.Sign(Vector3.Dot(traveller.PreviousOffsetFromPortal, transform.forward));

                if (portalSide != portalSideOld)
                {
                    var positionOld = travellerT.position;
                    var rotOld = travellerT.rotation;

                    traveller.Teleport(transform, linkedPortal.transform, m.GetColumn(3), m.rotation);

                    traveller.graphicsClone.transform.SetPositionAndRotation(positionOld, rotOld);

                    linkedPortal.OnTravellerEnterPortal(traveller);
                    trackedTravellers.RemoveAt(i);

                    i--;
                }
                else
                {
                    traveller.graphicsClone.transform.SetPositionAndRotation(m.GetColumn(3), m.rotation);


                    traveller.PreviousOffsetFromPortal = offsetFromPortal;
                }

                if (traveller.IsPlayer)
                AddDeformForce(travellerT.position);

                foreach (GameObject clippedObject in clippedObjects)
                {
                    clippedObject.SetActive(SameSideOfPortal(clippedObject.transform.position, travellerT.position));
                }
            }
        }
        private void OnTravellerEnterPortal(PortalTraveller traveller)
        {
            if (!trackedTravellers.Contains(traveller))
            {
                traveller.EnterPortalThreshold();
                traveller.PreviousOffsetFromPortal = traveller.Target.position - transform.position;
                trackedTravellers.Add(traveller);

                if (traveller.IsPlayer)
                    AddDeformForce(traveller.Target.position);

                foreach (GameObject clippedObject in clippedObjects)
                {
                    clippedObject.SetActive(SameSideOfPortal(clippedObject.transform.position, traveller.Target.position));
                }
            }
        }
        private void AddDeformForce(Vector3 point)
        {
            portalInfo[Camera.StereoscopicEye.Left].meshDeformer.AddDeformingForce(point, deformPower, SideOfPortal(point) > 0);
            portalInfo[Camera.StereoscopicEye.Right].meshDeformer.AddDeformingForce(point, deformPower, SideOfPortal(point) > 0);
        }


        void UpdateSliceParams(PortalTraveller traveller, Camera.StereoscopicEye eye)
        {
            // Calculate slice normal
            int side = SideOfPortal(traveller.transform.position);
            Vector3 sliceNormal = transform.forward * -side;
            Vector3 cloneSliceNormal = linkedPortal.transform.forward * side;

            // Calculate slice centre
            Vector3 slicePos = transform.position;
            Vector3 cloneSlicePos = linkedPortal.transform.position;

            // Adjust slice offset so that when player standing on other side of portal to the object, the slice doesn't clip through
            float sliceOffsetDst = 0;
            float cloneSliceOffsetDst = 0;
            float screenThickness = -screens.GetChild(0).transform.localScale.z;

            bool playerSameSideAsTraveller = SameSideOfPortal(portalInfo[eye].eye.Camera.transform.position, traveller.transform.position);
            if (!playerSameSideAsTraveller)
            {
                sliceOffsetDst = -screenThickness;
            }
            bool playerSameSideAsCloneAppearing = side != linkedPortal.SideOfPortal(portalInfo[eye].eye.Camera.transform.position);
            if (!playerSameSideAsCloneAppearing)
            {
                cloneSliceOffsetDst = -screenThickness;
            }

            // Apply parameters
            for (int i = 0; i < traveller.originalMaterials.Length; i++)
            {
                traveller.originalMaterials[i].SetVector("sliceCentre", slicePos);
                traveller.originalMaterials[i].SetVector("sliceNormal", sliceNormal);
                traveller.originalMaterials[i].SetFloat("sliceOffsetDst", sliceOffsetDst);

                traveller.cloneMaterials[i].SetVector("sliceCentre", cloneSlicePos);
                traveller.cloneMaterials[i].SetVector("sliceNormal", cloneSliceNormal);
                traveller.cloneMaterials[i].SetFloat("sliceOffsetDst", cloneSliceOffsetDst);

            }

        }

        private void SetNearClipPlane(Camera.StereoscopicEye eye)
        {
            Transform clipPlane = transform;
            int dot = System.Math.Sign(Vector3.Dot(clipPlane.forward, transform.position - portalInfo[eye].camera.transform.position));

            Vector3 camSpacePos = portalInfo[eye].camera.worldToCameraMatrix.MultiplyPoint(clipPlane.position);
            Vector3 camSpaceNormal = portalInfo[eye].camera.worldToCameraMatrix.MultiplyVector(clipPlane.forward) * dot;
            float camSpaceDst = -Vector3.Dot(camSpacePos, camSpaceNormal) + nearClipOffset;

            if (Mathf.Abs(camSpaceDst) > nearClipLimit)
            {
                Vector4 clipPlaneCameraSpace = new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);
                portalInfo[eye].camera.projectionMatrix = portalInfo[eye].eye.Camera.CalculateObliqueMatrix(clipPlaneCameraSpace);
            }
            else
            {
                portalInfo[eye].camera.projectionMatrix = portalInfo[eye].eye.Camera.projectionMatrix;
            }
        }

        [ContextMenu("Center")]
        public void Center()
        {
            if (Physics.Raycast(transform.position, screens.transform.up, out RaycastHit upHitInfo) && Physics.Raycast(transform.position, -screens.transform.up, out RaycastHit downHitInfo) && Physics.Raycast(transform.position, -screens.transform.right, out RaycastHit leftHitInfo) && Physics.Raycast(transform.position, screens.transform.right, out RaycastHit rightHitInfo))
            {
                float left = leftHitInfo.distance;
                float right = rightHitInfo.distance;
                float up = upHitInfo.distance;
                float down = downHitInfo.distance;

                if (up > down)
                {
                    transform.position += screens.transform.up * (up - down) / 2f;
                }
                else
                {
                    transform.position -= screens.transform.up * (down - up) / 2f;
                }

                if (right > left)
                {
                    transform.position += screens.transform.right * (right - left) / 2f;
                }
                else
                {
                    transform.position -= screens.transform.right * (left - right) / 2f;
                }
            }
        }
        #endregion

        #region Inner Classes
        [Serializable] public class PortalInfoDictionary : SerializableDictionaryBase<Camera.StereoscopicEye, PortalInfo> { }

        [Serializable] public class PortalInfo
        {
            public MeshDeformer meshDeformer;
            public MeshRenderer screen;
            public Camera camera;
            public MeshFilter screenMeshFilter;
            [Space]
            public Eye eye;
            public Transform alias;

            [HideInInspector] public RenderTexture viewTexture;
        }
        #endregion
    }
}