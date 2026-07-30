# Guide utilisateur — LocalDub.NET

**Français** | [English](README.EN.md)

LocalDub.NET traduit et double localement une vidéo française vers l'anglais américain. Il conserve toujours la vidéo originale : les fichiers créés portent le suffixe `-EN`.

Ce guide concerne l'application principale de doublage. Pour gérer des profils de voix personnels, consultez le [guide du gestionnaire de voix](../LocalDub.Voices/README.md).

## Avant de commencer

Vous avez besoin de :

- Windows 64 bits ;
- le SDK .NET 10 ;
- une carte NVIDIA compatible ;
- Ollama installé et démarré ;
- une vidéo avec une piste audio.

La première fois, depuis la racine du projet, préparez les composants nécessaires :

```powershell
dotnet run --project src/LocalDub.NET -- setup
```

Cette opération télécharge les outils et modèles locaux. Elle peut être longue et nécessite une connexion Internet. Les doublages suivants restent locaux.

Vérifiez ensuite l'installation :

```powershell
dotnet run --project src/LocalDub.NET -- doctor
```

## Doublage guidé avec le menu

Placez-vous à la racine du projet et lancez simplement :

```powershell
dotnet run --project src/LocalDub.NET
```

Le programme pose les questions une par une :

1. **Vidéo source** — indiquez son chemin absolu ou un chemin relatif au dossier courant.
2. **Production** — choisissez une vidéo traduite complète, ou seulement le WAV traduit.
3. **Traitement de la bande-son** — choisissez la façon de traiter le son original.
4. **Profil vocal** — choisissez une voix déclarée dans `voices/profiles.json`.
5. **Référence vocale** — conservez celle du profil ou choisissez un WAV présent dans `voices`.
6. **Variante vocale** — neutre, stable ou expressive.
7. **Glossaire et termes à préserver** — utiles pour les acronymes, marques et termes spécialisés.
8. **Modèle de traduction** — choisissez le modèle Ollama proposé.

Avant le traitement, un récapitulatif est affiché. Répondez `o` pour confirmer ou toute autre réponse pour annuler.

### Chemins acceptés

Les chemins de vidéo et de WAV peuvent être :

- absolus, par exemple `D:\Videos\presentation.mp4` ;
- relatifs au dossier depuis lequel vous lancez la commande, par exemple `videos\presentation.mp4`.

Si un chemin contient des espaces, entourez-le de guillemets lorsque vous le fournissez sur la ligne de commande.

## Choisir le type de production

### Production complète — vidéo traduite

Produit une nouvelle vidéo `-EN` contenant l'image d'origine et une nouvelle piste audio anglaise. Le WAV, les sous-titres et le manifeste sont également créés.

Choisissez cette option pour obtenir une vidéo prête à visionner ou à publier.

### WAV traduit uniquement

Produit le WAV anglais synchronisé, les sous-titres et le manifeste, mais ne reconstruit pas de vidéo finale.

Choisissez cette option si vous effectuez ensuite le mixage ou le montage dans DaVinci Resolve, Premiere ou un autre logiciel. Le WAV commence à `00:00:00` et a la durée de la vidéo source.

## Choisir le traitement de la bande-son

### Séparer la voix française et conserver la musique

Option recommandée pour une vidéo parlée avec fond musical. Le programme tente de retirer la voix française tout en préservant l'accompagnement, puis ajoute la voix anglaise.

Sur des contenus musicaux, un chant, un vocodeur ou un son proche d'une voix peut être mal séparé. Contrôlez le résultat avant publication.

### Atténuer le son original pendant la voix anglaise

La bande-son originale reste présente, mais son niveau diminue lorsque la voix anglaise parle. C'est souvent plus sûr pour les démonstrations musicales, au prix d'une possible présence résiduelle de la voix française.

### Voix anglaise seule pour un mixage externe

La vidéo produite contient uniquement la voix anglaise. Le WAV `-EN.wav` peut être utilisé dans votre logiciel de montage pour réaliser le mixage final.

## Choisir une voix

Les profils intégrés incluent Michael, Adam et la voix Chatterbox intégrée. Vous pouvez créer votre propre profil avec le gestionnaire de voix :

```powershell
dotnet run --project src/LocalDub.Voices
```

Dans le menu principal, après le choix du profil, la liste des WAV détectés dans `voices` est proposée. Vous pouvez donc utiliser ponctuellement un WAV personnel, même sans créer immédiatement de profil JSON.

Les variantes vocales ont l'effet suivant :

- **Neutre** : utilise les réglages du profil ; bon choix par défaut.
- **Stable** : narration plus régulière et prévisible.
- **Expressive** : rendu plus vivant, mais parfois moins constant.

## Fichiers créés

Pour une source `D:\Videos\demo.mp4`, LocalDub crée à côté de la vidéo :

```text
demo-EN.mp4   vidéo traduite, seulement en production complète
demo-EN.wav   voix anglaise synchronisée
demo-EN.srt   sous-titres anglais
demo-EN.json  segments, traductions, timings et paramètres retenus
```

Les fichiers intermédiaires de reprise sont placés dans `output/work/`. Ils permettent de reprendre plus rapidement un traitement interrompu.

Une sortie existante bloque le lancement par défaut. Dans le menu, le programme demande confirmation pour la remplacer. La vidéo source n'est jamais remplacée.

## Lancer sans menu

Vous pouvez fournir les paramètres directement. Exemple de production vidéo complète :

```powershell
dotnet run --project src/LocalDub.NET -- dub `
  --input "D:\Videos\demo.mp4" `
  --production video `
  --audio-mode separate `
  --voice michael-us `
  --voice-variant stable `
  --glossary ai `
  --preserve "SRP,SOLID,Semantic Kernel" `
  --model gemma4:12b `
  --yes
```

Pour produire seulement le WAV synchronisé :

```powershell
dotnet run --project src/LocalDub.NET -- dub `
  --input "D:\Videos\demo.mp4" `
  --production wav `
  --audio-mode separate `
  --voice michael-us `
  --yes
```

Ajoutez `--overwrite` si vous souhaitez remplacer les fichiers `-EN` existants. Cette option ne peut jamais modifier la vidéo source.

## Aide rapide en cas de problème

- Lancez `doctor` si un outil ou un environnement semble manquer.
- Vérifiez qu'Ollama est démarré et que le modèle sélectionné est disponible.
- Vérifiez que le WAV de référence existe toujours dans `voices`.
- Pour une vidéo très musicale, essayez le mode d'atténuation ou exportez seulement le WAV afin de refaire le mixage dans votre logiciel habituel.
- En cas d'interruption, relancez le même traitement : les transcriptions et segments déjà calculés sont réutilisés.
