using UnityEngine;

public class kao : MonoBehaviour
{
    private void OnStart()
    {
        InvokeRepeating(nameof(RotateAndReset), 0f, 5f);
    }
    private void OnStop()
    {
        CancelInvoke(nameof(RotateAndReset));
    }

    private void RotateAndReset()
    {
        StartCoroutine(RotateCoroutine());
    }

    private System.Collections.IEnumerator RotateCoroutine()
    {
        Quaternion originalRotation = transform.rotation;
        Quaternion targetRotation = originalRotation * Quaternion.Euler(0, 60, 0);

        float elapsedTime = 0f;
        float duration = 0.5f; // Time to complete the rotation

        while (elapsedTime < duration)
        {
            transform.rotation = Quaternion.Slerp(originalRotation, targetRotation, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.rotation = targetRotation;

        yield return new WaitForSeconds(0.5f); // Pause before returning to original rotation

        elapsedTime = 0f;
        while (elapsedTime < duration)
        {
            transform.rotation = Quaternion.Slerp(targetRotation, originalRotation, elapsedTime / duration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        transform.rotation = originalRotation;
    }
}
