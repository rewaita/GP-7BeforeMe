using UnityEngine;

public class moveCamera : MonoBehaviour
{
    public GameObject Pl;
    public GameObject AIpl;
    void Update()
    {
        if (Pl.activeSelf)
        {
            Vector3 PlPosition = Pl.transform.position;
            transform.position = new Vector3(PlPosition.x, PlPosition.y + 40, PlPosition.z);
            transform.LookAt(Pl.transform);
        }
        else if (AIpl.activeSelf)
        {
            Vector3 AIplPosition = AIpl.transform.position;
            transform.position = new Vector3(AIplPosition.x, AIplPosition.y + 40, AIplPosition.z);
            transform.LookAt(AIpl.transform);
        }
    }
}
