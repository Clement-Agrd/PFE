using UnityEngine;

namespace DustVFX.Demo
{
    /// <summary>Tiny follow camera with a third-person and a side view (used by the capture runner).</summary>
    [AddComponentMenu("Dust VFX/Demo Camera")]
    public sealed class DustDemoCamera : MonoBehaviour
    {
        public enum View { ThirdPerson, Side, Low }

        public Transform target;
        public View view = View.ThirdPerson;
        [Min(0f)] public float follow = 8f;

        [Header("Third person")]
        public float tpDistance = 5.2f;
        public float tpHeight = 2.4f;
        public float tpLookHeight = 0.6f;

        [Header("Side view")]
        public float sideDistance = 4.2f;
        public float sideHeight = 1.0f;

        Vector3 smoothed;
        float yaw;
        bool init;

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 t = target.position;
            if (!init) { smoothed = t; init = true; }
            smoothed = Vector3.Lerp(smoothed, t, follow > 0f ? 1f - Mathf.Exp(-follow * Time.deltaTime) : 1f);

            Vector3 flatFwd = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
            if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.forward;

            Vector3 pos;
            Vector3 look = smoothed + Vector3.up * tpLookHeight;
            switch (view)
            {
                case View.Side:
                    // Perpendicular to the +X run axis, so the cloud trailing behind is seen in profile.
                    pos = smoothed + new Vector3(0f, sideHeight, -sideDistance);
                    look = smoothed + Vector3.up * 0.55f;
                    break;
                case View.Low:
                    pos = smoothed - flatFwd * 3.2f + Vector3.up * 0.45f + Vector3.Cross(Vector3.up, flatFwd) * 0.8f;
                    look = smoothed + Vector3.up * 0.7f;
                    break;
                default:
                    float targetYaw = Mathf.Atan2(flatFwd.x, flatFwd.z) * Mathf.Rad2Deg;
                    yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
                    Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
                    pos = smoothed + back * tpDistance + Vector3.up * tpHeight;
                    break;
            }

            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, Vector3.up));
        }

        public void SnapTo(Vector3 targetPos)
        {
            smoothed = targetPos;
            init = true;
            yaw = target != null ? target.eulerAngles.y : 0f;
        }
    }
}
