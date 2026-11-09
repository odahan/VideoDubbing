# Manuel utilisateur de LocalDub OpenAI

**Version 10.0.0** · [English](MANUAL.EN.md) · [README de l’édition](README.FR.md)

## Installation

Prévoyez Windows 64 bits, le SDK .NET 10 et une carte NVIDIA avec un pilote récent. Il vous
faut aussi un compte ChatGPT qui peut autoriser l’utilisation du forfait et une connexion
Internet pour l’authentification et les requêtes texte. L’édition réutilise FFmpeg, whisper.cpp,
Demucs et Chatterbox depuis `.localdub/` à la racine du dépôt. Si cette installation est déjà
prête, passez directement au lancement. Sinon, exécutez l’installation audio standard à la
racine du dépôt.

```powershell
dotnet run --project .\src\LocalDub.NET -- setup
```

L’installation standard prépare Chatterbox-Turbo et ses profils par défaut. Un profil
personnalisé Chatterbox Multilingual V3 requiert son propre environnement local, ses poids et
son WAV de référence. Sélectionnez une voix anglaise pour cette édition. Conservez vos
enregistrements de référence sur votre machine. Lancez les deux éditions à tour de rôle,
car elles utilisent les mêmes ports locaux de synthèse.

## Connexion et modèles

Ouvrez `RunMenuWithOpenAI.BAT` à la racine du dépôt. Si aucune session n’est active, acceptez
la proposition de connexion ou choisissez **Connect to ChatGPT** dans le menu. Suivez
**Continue with ChatGPT** dans le navigateur et autorisez l’application à utiliser votre forfait.
La session est conservée dans le stockage protégé de Windows pour votre compte utilisateur.

**Choose models** affiche les modèles proposés par le compte connecté. Le modèle de traduction
traite le texte français regroupé. Le modèle de reformulation raccourcit les phrases et peut
varier deux formulations anglaises successives. Vous pouvez choisir le même modèle pour les
deux tâches. Le menu préfère `gpt-6-luna` s’il est disponible et conserve vos choix jusqu’à
sa fermeture.

**Reset ChatGPT connection** supprime l’enregistrement et les identifiants locaux et tente
de révoquer l’accès côté OpenAI. Pour changer de compte, réinitialisez la connexion, puis
connectez-vous avec l’autre compte. OpenAI reçoit la narration française, le texte anglais
à reformuler, les termes du glossaire et les contraintes de durée. Les médias et les WAV
de référence restent sur la machine.

## Doubler une vidéo

1. Choisissez **Dub a video** et saisissez le chemin de la vidéo source. Vous pouvez entourer le chemin de guillemets.
2. Choisissez **Complete video, WAV and subtitles** pour une vidéo complète ou **Dubbed WAV and subtitles** pour la piste de voix et les sous-titres.
3. Choisissez le mode audio par numéro. Il détermine aussi la piste que Whisper utilise lorsqu’une transcription est nécessaire.
4. Réglez la vitesse de base pour cette vidéo. Appuyez sur Entrée pour `1,00`, ou saisissez `1,10` si la vidéo a déjà été accélérée de 10 %. Le menu accepte la virgule et le point décimaux.
5. Sélectionnez le profil de voix, la référence et la variante. **Neutral** reprend les réglages du profil. **Stable** et **Expressive** appliquent des réglages prédéfinis.
6. Sélectionnez un glossaire par numéro, **No glossary**, ou indiquez un autre fichier JSON. Saisissez les graphies supplémentaires à conserver, séparées par des virgules, par exemple `VST, BBCUT, LIVECUT`.
7. Laissez **Existing LocalDub JSON for comparison** vide pour repartir de l’audio. Pour réutiliser une transcription française, suivez la procédure de comparaison ci-dessous.
8. Confirmez le remplacement éventuel des anciens exports OpenAI, puis le lancement. Les chemins s’affichent à la fin.

| Mode audio | Source de la transcription locale | Son de la vidéo exportée |
| --- | --- | --- |
| 1. Separate | Voix isolée par Demucs | Voix doublée et accompagnement isolé de l’original |
| 2. Duck | Bande-son originale | Voix doublée sur la bande-son originale, atténuée pendant la narration |
| 3. External mix | Bande-son originale | Voix anglaise seule pour le mixage dans un logiciel de montage |

Le WAV séparé contient la voix anglaise dans les trois modes. Pour un montage existant,
choisissez **Dubbed WAV and subtitles** et **External mix**, puis placez le WAV au début
de la timeline source. Il conserve le silence initial et couvre la durée de la vidéo.

## Phrases, vitesse et sous-titres

L’édition rassemble les fragments d’une même phrase française avant de la traduire. Lorsque
les repères de mots de Whisper correspondent au texte source, ils permettent de caler le début
d’une phrase à l’intérieur d’une entrée SRT. Si les repères sont insuffisants, l’entrée reste
entière. Les blocs longs sont découpés si nécessaire à une limite de proposition ou de sous-titre
utilisable. Chatterbox synthétise chaque unité obtenue d’un seul tenant.

La vitesse de base s’applique à chaque clip. La marge habituelle est de 15 % au-dessus de cette
base. À `1,10`, la reformulation commence donc au-delà d’environ `1,265` au total. Après
reformulation, le pipeline peut accélérer davantage si la phrase dépasse toujours son créneau.
Il s’arrête avec l’identifiant de la phrase si le plafond absolu configuré serait dépassé.
Le blanc éventuel se trouve après la phrase. Les pauses entre les unités restent sur la timeline.

Les sous-titres anglais longs sont découpés après la synthèse. Leurs repères intermédiaires
sont estimés selon la quantité de texte dans la fenêtre parlée. Le découpage des sous-titres
n’ajoute aucune pause à la voix. Les graphies `VST`, `VSTs` et `VST3` sont conservées dans
les exports et reçoivent une prononciation distincte dans Chatterbox.

## Comparer avec un rendu précédent

Pour partir du même français, renseignez le chemin d’un ancien manifeste `-EN.json` ou d’un
fichier de travail `segments.json` au champ de comparaison. L’édition reprend le français et
les repères, puis retraduit les phrases regroupées. Elle peut utiliser Whisper en local pour
retrouver les repères de mots dans les entrées importées. Les répétitions suspectes peuvent
aussi déclencher une vérification locale avant le regroupement.

Pour comparer à l’écoute, gardez la même vidéo, la même voix, la même référence, le même
glossaire, la même vitesse de base et le même mode audio. Les fichiers de cette édition
portent le suffixe `-EN-OpenAI`.

## Exports et reprise

Pour `demo.mp4`, vous trouverez à côté de la source `demo-EN-OpenAI.wav`,
`demo-EN-OpenAI.srt` et `demo-EN-OpenAI.json`. Le mode vidéo crée aussi
`demo-EN-OpenAI.mp4`. Le manifeste indique les modèles, les identifiants des entrées
source, les fenêtres source et parlées, les durées synthétisées et les vitesses appliquées.

Le cache de travail se trouve sous `output/openai/work/`. Les lots de traduction terminés
y sont sauvegardés. Relancez avec les mêmes réglages pour les réutiliser après une interruption.
Un changement de modèle, de glossaire, de référence ou de vitesse de base peut sélectionner un
autre dossier de travail. Si une phrase dépasse le plafond de vitesse, repérez son identifiant
dans le fichier de travail `segments.json`. Vous pouvez y corriger la traduction anglaise et
relancer avec les mêmes réglages.

| Symptôme | Vérification |
| --- | --- |
| Outil ou modèle local introuvable | Lancez **Check shared audio installation**. Pour les profils standard, exécutez l’installation commune si des fichiers manquent. |
| Connexion ou autorisation absente | Terminez la connexion dans le navigateur et autorisez l’utilisation du forfait. Réinitialisez la connexion pour changer de compte. |
| Modèle indisponible ou quota atteint | Consultez le catalogue dans **Choose models** et vérifiez l’usage dans ChatGPT avant de reprendre. |
| Fichier de sortie déjà présent | Autorisez son remplacement, ou déplacez-le d’abord si vous souhaitez garder les deux rendus. |
| Réponse OpenAI inattendue | Relevez l’erreur de la console et l’identifiant de requête s’il est affiché. Gardez vos identifiants de connexion privés. |

## Ligne de commande

Lancez ces exemples depuis la racine du dépôt. Connectez-vous avant de démarrer un doublage.
La commande `models` donne les identifiants à passer à `--model` et `--timing-model`.

```powershell
dotnet run --project .\srcOpenAI -- --version
dotnet run --project .\srcOpenAI -- doctor
dotnet run --project .\srcOpenAI -- sign-in
dotnet run --project .\srcOpenAI -- models
dotnet run --project .\srcOpenAI -- dub --input "demo.mp4" --production wav --audio-mode external-mix --base-speed 1.10 --glossary synths --preserve "VST,BBCUT"
dotnet run --project .\srcOpenAI -- dub --input "demo.mp4" --transcript "demo-EN.json" --production wav --audio-mode external-mix
dotnet run --project .\srcOpenAI -- sign-out
```

Utilisez `--help` pour afficher toutes les options. Si vous lancez l’exécutable compilé hors
du dépôt et que la détection automatique échoue, indiquez le chemin de l’installation avec
`--shared-root`.
