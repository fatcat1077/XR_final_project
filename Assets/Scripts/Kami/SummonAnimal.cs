using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SummonAnimal : MonoBehaviour
{
    public GameObject[] animals;
    private bool isSummoning;
    void Start()
    {
        isSummoning = true;
        StartCoroutine(StartSummon());
    }
    IEnumerator StartSummon()
    {
        while (isSummoning)
        {
            GameObject animal = Instantiate(animals[Random.Range(0, animals.Length)], new Vector3(0,-10,0), transform.rotation);
            float randomScale = Random.Range(0.5f, 2.0f);

            animal.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
            Rigidbody rb = animal.GetComponent<Rigidbody>();

            if (rb != null)
            {
                // 1. 先把隨機的「方向」算出來並存到變數裡 (注意要加 f 變成小數)
                Vector3 forceDirection = new Vector3(
                    Random.Range(-1f, 1f), 
                    Random.Range(-1f, 1f), 
                    Random.Range(-1f, 1f)
                ).normalized;

                // 2. 讓物件的「正前方」轉向這個受力方向
                // 加一個判斷避免方向剛好是 (0,0,0) 時 Unity 會報錯
                if (forceDirection != Vector3.zero) 
                {
                    animal.transform.rotation = Quaternion.LookRotation(forceDirection);
                }

                // 3. 對這個方向施加推力
                rb.AddForce(forceDirection * 5f, ForceMode.Impulse); 
            }

            yield return new WaitForSeconds(Random.Range(1, 3));
        }
    }
}
