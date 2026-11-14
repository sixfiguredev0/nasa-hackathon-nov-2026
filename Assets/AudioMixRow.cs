using RedFrontier.Audio;
using UnityEngine;

// Three compact category sliders (music, effects, ambience) shown beside the existing master volume.
// Values persist through AudioPrefs; master volume stays on FrontierUi.MasterVolume.
internal sealed class AudioMixRow
{
    public const float Height = 58;
    const int Steps = 10;
    static readonly AudioBus[] Buses = { AudioBus.Music, AudioBus.Effects, AudioBus.Ambience };
    static readonly string[] Names = { "Music", "Effects", "Ambience" };

    public readonly RectTransform Root;
    public readonly PixelSlider[] Sliders = new PixelSlider[3];
    readonly PixelLabel[] labels = new PixelLabel[3];

    public AudioMixRow(Transform parent)
    {
        Root = FrontierUi.Box("Audio mix", parent);
        var size = new Vector2(100, Height);
        for (int i = 0; i < Buses.Length; i++)
        {
            int index = i;
            labels[i] = PixelLabel.Create(Names[i] + " caption", Root, "", 2, FrontierUi.Cream, 0, true);
            Sliders[i] = FrontierUi.Slider(Names[i] + " volume", Root, Steps);
            FrontierUi.Place(labels[i].Rect, size, i * 35, 0, 30, 24);
            FrontierUi.Place((RectTransform)Sliders[i].transform, size, i * 35, 30, 30, 28);
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

    void UpdateLabel(int i) => labels[i].Text = Names[i] + " " + Mathf.RoundToInt(Sliders[i].value * 100 / Steps) + "%";
}
