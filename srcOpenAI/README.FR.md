# LocalDub OpenAI 10.0.0

**Français** | [English](README.md)

[Manuel en français](MANUAL.FR.md) · [English user manual](MANUAL.EN.md) · [README du dépôt](../README.FR.MD)

LocalDub OpenAI double des vidéos françaises en anglais américain. Un compte ChatGPT connecté
sert à traduire et à raccourcir les phrases. Whisper, Chatterbox, Demucs et FFmpeg utilisent
l’installation audio locale partagée avec LocalDub.NET.

## Démarrage rapide

1. Préparez l’installation audio commune si elle est absente. Les commandes et les prérequis figurent dans le manuel.
2. Lancez `RunMenuWithOpenAI.BAT` à la racine du dépôt, puis autorisez l’utilisation de votre forfait dans le navigateur.
3. Dans **Choose models**, choisissez les modèles proposés par le compte pour la traduction et la reformulation. Le menu préfère Luna lorsqu’il est disponible.
4. Choisissez **Dub a video**. Les modes audio, les voix et les glossaires se sélectionnent par numéro. Pour une nouvelle vidéo, laissez le champ JSON vide.

Le français est regroupé en phrases avant la traduction. Une phrase qui traverse plusieurs
entrées SRT est synthétisée d’un seul tenant. Pour une vidéo déjà accélérée de 10 %, saisissez
`1,10` dans **Base narration speed**. Laissez le champ vide pour conserver `1,00`.

Les exports portent le suffixe `-EN-OpenAI`. En mode WAV, la piste contient la voix anglaise
seule et peut être importée au début de la timeline dans un logiciel de montage. Le manuel
détaille les modes audio, le cache et les options de reprise.

La connexion et le coffre Windows ont été adaptés de [TextAid](https://github.com/odahan/TextAid)
par Olivier Dahan, sous [licence CC BY-NC 4.0](https://github.com/odahan/TextAid/blob/main/Licence.md).
