using UnityEngine;

public class Billboard : MonoBehaviour
{
    private Transform _cam;

    private void OnEnable()
    {
        if (Camera.main != null) 
            _cam = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (_cam == null)
        {
            if (Camera.main == null) return;
            _cam = Camera.main.transform;
        }
        
        transform.rotation = _cam.rotation;
    }
}