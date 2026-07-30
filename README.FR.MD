# LocalDub.NET

**Français** | [English](README.md)

LocalDub.NET est un pipeline local de doublage français vers anglais US pour les vidéos à un seul locuteur. L’orchestration est écrite en C#/.NET ; FFmpeg, whisper.cpp, Ollama et Chatterbox s’exécutent localement. Aucune donnée de la vidéo n’est envoyée à un service cloud.

La vidéo d’origine n’est jamais modifiée. Pour `demo.mp4`, LocalDub crée `demo-EN.mp4` à côté de la source ainsi que :

- `demo-EN.wav` : voix anglaise complète, calée depuis `00:00:00`, PCM 48 kHz/24 bits ;
- `demo-EN.srt` : sous-titres anglais ;
- `demo-EN.json` : segments, traductions, timings et paramètres du traitement.

## Guides utilisateur

Choisissez votre langue :

- [Guide utilisateur en français](src/LocalDub.NET/README.md)
- [English user guide](src/LocalDub.NET/README.EN.md)

L’outil séparé de gestion des profils vocaux possède aussi sa [documentation](src/LocalDub.Voices/README.md).

## Pipeline

1. Extraction d’un master audio 48 kHz avec FFmpeg.
2. Séparation facultative de la voix et de l’accompagnement avec Demucs.
3. Conversion de la parole en mono 16 kHz puis transcription française avec whisper.cpp.
4. Traduction contextuelle par lots avec OllamaSharp et Microsoft Agent Framework.
5. Synthèse anglaise segmentée avec Chatterbox-Turbo.
6. Reformulation des phrases trop longues, puis accélération habituellement limitée à 1,15×.
7. Placement de chaque phrase à son timestamp et création d’un WAV de la durée exacte de la vidéo.
8. Création du SRT et d’une nouvelle vidéo `-EN` sans réencoder l’image.

Le traducteur principal est déchargé avant Chatterbox afin de libérer la VRAM. Les éventuelles reformulations utilisent ensuite le modèle léger `granite4:3b`.

## Prérequis

- Windows 64 bits ;
- SDK .NET 10 ;
- carte NVIDIA avec pilote récent ;
- Ollama installé et démarré ;
- environ 20 à 30 Go libres pour les outils, environnements et modèles.

La configuration fournie utilise `gemma4:12b`. Un autre modèle Ollama peut être choisi dans `appsettings.json` ou avec `--model`.

## Installation automatique

Depuis la racine du dépôt :

```powershell
dotnet run --project src/LocalDub.NET -- setup
```

Cette commande installe dans `.localdub/`, sans modifier les installations CUDA ou Ollama existantes : FFmpeg, whisper.cpp CUDA et `large-v3-turbo`, `uv` et trois environnements Python 3.11 isolés, Chatterbox-Turbo, Demucs, Kokoro et deux références masculines américaines générées localement.

Une connexion Internet est nécessaire pendant cette première préparation. Les doublages suivants fonctionnent hors ligne. La commande est relançable après interruption : les outils et modèles prêts sont conservés et le traitement reprend à la première étape incomplète.

Vérification :

```powershell
dotnet run --project src/LocalDub.NET -- doctor
```

## Choix d’une voix

Le setup génère deux références masculines américaines utilisées par Chatterbox-Turbo :

- `michael-us` : voix grave et professionnelle, profil par défaut ;
- `adam-us` : voix plus énergique et affirmée ;
- `narrator-us` : voix féminine intégrée d’origine de Chatterbox.

Auditionner un profil :

```powershell
dotnet run --project src/LocalDub.NET -- voices audition --voice michael-us
```

Vous pouvez employer une référence personnelle propre de 6 à 10 secondes, en français ou en anglais, avec une seule voix et sans musique ni réverbération. Placez-la dans `voices/`, utilisez-la ponctuellement dans l’assistant, ou créez un profil réutilisable avec le gestionnaire de voix :

```powershell
dotnet run --project src/LocalDub.Voices
```

## Doublage interactif

Lancez LocalDub sans argument pour afficher l’assistant :

```powershell
dotnet run --project src/LocalDub.NET
```

Il demande la vidéo, le type de production, le traitement audio, la voix, le glossaire, les termes à préserver et le modèle Ollama, puis affiche la sortie prévue avant confirmation. Vous pouvez préremplir seulement le chemin :

```powershell
dotnet run --project src/LocalDub.NET -- dub --input "D:\Videos\demo.mp4"
```

## Doublage automatisé

```powershell
dotnet run --project src/LocalDub.NET -- dub `
  --input "D:\Videos\demo.mp4" `
  --audio-mode separate `
  --production video `
  --voice michael-us `
  --voice-variant stable `
  --glossary ai `
  --preserve "SRP,SOLID,DRY,MAF,Semantic Kernel" `
  --model gemma4:12b `
  --yes
```

Une sortie `-EN` existante bloque l’exécution. `--overwrite` autorise uniquement son remplacement ; la vidéo source reste protégée. Ajoutez `--production wav` pour créer seulement le WAV anglais calé, le SRT et le JSON, sans reconstruire de vidéo.

## Modes audio

### `separate`

Demucs estime une piste vocale et une piste d’accompagnement, retire la voix française puis ajoute la voix anglaise. Ce mode convient généralement aux vidéos parlées sur fond musical. La séparation n’est pas parfaite : sur un contenu musical, un vocodeur, un chœur ou un son proche d’une voix peut aussi être retiré. Les stems restent dans `output/work/` pour contrôle.

### `duck`

La piste originale est fortement atténuée pendant la voix anglaise, puis remonte progressivement. Ce mode préserve mieux les démonstrations musicales, mais la voix française peut rester légèrement audible.

### `external-mix`

La nouvelle vidéo contient uniquement la piste anglaise. Le WAV `-EN.wav`, calé depuis zéro, peut être posé à `00:00:00` dans DaVinci Resolve ou un autre logiciel pour refaire le mixage final avec les pistes originales.

## Glossaires et reprise après interruption

Deux exemples sont fournis :

- `glossaries/ai.json` pour l’informatique, MAF et l’IA ;
- `glossaries/synths.json` pour la musique et les synthétiseurs.

Utilisez le nom du fichier sans extension avec `--glossary`. `--preserve` ajoute des termes propres à une vidéo sans modifier le JSON.

Les transcriptions, stems et traductions sont conservés dans `output/work/`. Une relance avec la même vidéo et les mêmes options les réutilise ; le dossier de travail change automatiquement si la vidéo, le mode audio, le modèle Ollama ou le glossaire change.

## Compilation et tests

```powershell
dotnet restore LocalDub.NET.slnx -m:1
dotnet build LocalDub.NET.slnx --configuration Release -m:1
dotnet test LocalDub.NET.slnx --configuration Release -m:1
```

## Limites de la V1

- un seul locuteur ;
- français vers anglais US ;
- synchronisation contrainte par phrase, sans modification visuelle des lèvres ;
- qualité de séparation dépendante du contenu ;
- installation automatique ciblée Windows/NVIDIA.

## Licences et monétisation

Le code du projet peut être utilisé séparément selon sa propre licence. Les outils et modèles conservent leurs licences respectives. Chatterbox et ses poids officiels sont annoncés sous licence MIT ; whisper.cpp est sous licence MIT. Vérifiez toujours les licences des modèles Ollama et des références vocales que vous choisissez avant une publication monétisée.
