namespace URPLabStudio
{
using UnityEngine;

public sealed class Lab_PostSelection : MonoBehaviour
{
    public GameObject[] Posts;
    public int selectedPost;

    public void NextPost()
    {
        if (Posts == null || Posts.Length == 0) return;
        Posts[selectedPost].SetActive(false);
        selectedPost = (selectedPost + 1) % Posts.Length;
        Posts[selectedPost].SetActive(true);
    }

    public void PreviousPost()
    {
        if (Posts == null || Posts.Length == 0) return;
        Posts[selectedPost].SetActive(false);
        selectedPost = (selectedPost - 1 + Posts.Length) % Posts.Length;
        Posts[selectedPost].SetActive(true);
    }
}
}
