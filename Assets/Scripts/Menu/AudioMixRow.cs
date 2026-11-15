using RedFrontier.Audio;
using UnityEngine;

// Three compact category sliders (music, effects, ambience) shown beside the existing master volume.
// Values persist through AudioPrefs; master volume stays on FrontierUi.MasterVolume.
internal sealed class AudioMixRow
{
    public const float Height = 58;
    public const float StackedHeight = 68;
    const int Steps = 10;
    static readonly AudioBus[] Buses = { AudioBus.Music, AudioBus.Effects, AudioBus.Ambience };
    static readonly string[] Names = { "Music", "Effects", "Ambience" };

    public readonly RectTransform Root;
    public readonly PixelSlider[] Sliders = new PixelSlider[3];
    readonly PixelLabel[] labels = new PixelLabel[3];
    readonly bool stackedCaptions;

    public AudioMixRow(Transform parent, bool stackedCaptions = false)
    {
        this.stackedCaptions = stackedCaptions;
        Root = FrontierUi.Box("Audio mix", parent);
        var size = new Vector2(100, stackedCaptions ? StackedHeight : Height);
        float captionHeight = stackedCaptions ? 44 : 24;
        for (int i = 0; i < Buses.Length; i++)
        {
            int index = i;
            labels[i] = PixelLabel.Create(Names[i] + " caption", Root, "", 2, FrontierUi.Cream, 0, true);
            Sliders[i] = FrontierUi.Slider(Names[i] + " volume", Root, Steps);
            FrontierUi.Place(labels[i].Rect, size, i * 35, 0, 30, captionHeight);
            FrontierUi.Place((RectTransform)Sliders[i].transform, size, i * 35,
                stackedCaptions ? 44 : 30, 30, stackedCaptions ? 24 : 28);
            Sliders[i].onValueChanged.AddListener(value =>
            {
                GameAudio.SetLevel(Buses[index], value / Steps);
                UpdateLabel(index);
            });
        }
        Refresh();
    }

    public void Refresh()
    {
        for (int i = 0; i < Buses.Length; i++)
        {
            Sliders[i].SetValueWithoutNotify(Mathf.Round(GameAudio.GetLevel(Buses[i]) * Steps));
            UpdateLabel(i);
        }
    }

    void UpdateLabel(int i) => labels[i].Text = Names[i] + (stackedCaptions ? "\n" : " ")
        + Mathf.RoundToInt(Sliders[i].value * 100 / Steps) + "%";
}
