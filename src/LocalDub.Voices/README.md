# Gestionnaire de voix LocalDub

`LocalDub.Voices` est un utilitaire de console séparé de l'application de doublage. Il sert à gérer les profils déclarés dans `voices/profiles.json` et à comparer le rendu de plusieurs réglages de synthèse avant de les retenir.

Il ne modifie jamais une vidéo et ne supprime jamais de fichier WAV automatiquement. Avant chaque modification de `profiles.json`, il crée une copie de sauvegarde horodatée dans le dossier `voices`, par exemple `voices/profiles.backup-20260723-142530-123.json`.

## Démarrer l'utilitaire

Ouvrez PowerShell à la racine du dépôt, c'est-à-dire le dossier qui contient `appsettings.json` et le dossier `voices`, puis exécutez :

```powershell
dotnet run --project src/LocalDub.Voices
```

L'utilitaire affiche un menu :

```text
1. Lister les profils
2. Ajouter une voix personnelle
3. Générer des auditions (neutre / stable / expressive)
4. Appliquer un préréglage à un profil
5. Supprimer un profil (le WAV est conservé)
0. Quitter
```

## Chemins de fichiers

Lorsqu'un chemin WAV est demandé, vous pouvez indiquer :

- un chemin absolu, par exemple `D:\Enregistrements\ma-voix.wav` ;
- un chemin relatif au dossier depuis lequel vous avez lancé la commande, par exemple `voices\ma-voix-source.wav` ou `..\Enregistrements\ma-voix.wav`.

Le fichier doit exister et avoir l'extension `.wav`. Lors de l'ajout d'un profil, l'utilitaire le copie vers `voices/<identifiant>.wav`. Le profil enregistré dans le JSON utilise alors un chemin relatif au projet, par exemple `voices/odile.wav`. Il est préférable de ne pas modifier ou déplacer ce fichier ensuite.

Pour une bonne référence, enregistrez 6 à 10 secondes de voix seule, propre, sans musique, réverbération ni bruit important. Un extrait français naturel convient ; un extrait anglais peut légèrement aider la prononciation anglaise, mais n'est pas requis.

## Les réglages proposés

Les valeurs `temperature`, `topP` et `repetitionPenalty` ne sont pas des caractéristiques mesurées du WAV. Elles règlent la façon dont Chatterbox produit la voix :

- **Neutre** : conserve les réglages actuels du profil. C'est généralement le bon point de départ.
- **Stable** : `temperature 0.65`, `topP 0.90`, `repetitionPenalty 1.25`. Convient à une narration régulière et prévisible.
- **Expressif** : `temperature 0.95`, `topP 0.98`, `repetitionPenalty 1.10`. Donne davantage de variation et de vie, avec parfois plus de risques d'irrégularités.

La bonne méthode consiste à écouter les trois rendus, puis à enregistrer celui qui convient le mieux à votre voix et au type de contenu. Le réglage `topK` est conservé depuis le profil servant de modèle ; sa valeur habituelle est `1000`.

## Exemple : ajouter une voix personnelle

Cet exemple crée un profil nommé `odile` à partir du fichier `D:\Audio\odile-reference.wav`.

1. Lancez l'utilitaire depuis la racine du projet.

   ```powershell
   dotnet run --project src/LocalDub.Voices
   ```

2. Saisissez `2` pour **Ajouter une voix personnelle**.

3. À la question **Identifiant court**, saisissez `odile`.

   L'identifiant accepte uniquement les lettres minuscules, chiffres et tirets. Il sert au nom du profil et du fichier copié : `voices/odile.wav`.

4. À la question **Nom affiché**, saisissez par exemple `Odile — voix personnelle`.

5. À la question **Chemin du WAV de référence**, saisissez :

   ```text
   D:\Audio\odile-reference.wav
   ```

6. Lorsque l'utilitaire demande le **profil de réglages à copier**, choisissez `Michael US — grave et professionnel` comme point de départ. Cela n'imite pas sa voix : seuls les réglages techniques sont repris. La référence audio utilisée sera bien votre WAV.

7. Le programme confirme la création du profil et la copie vers `voices/odile.wav`.

8. Dans le menu, choisissez `3` pour générer des auditions et sélectionnez le profil `odile`. Vous pouvez conserver le texte anglais proposé ou saisir votre propre phrase anglaise.

9. Écoutez les fichiers créés dans :

   ```text
   voices\previews\odile\odile-neutral.wav
   voices\previews\odile\odile-stable.wav
   voices\previews\odile\odile-expressive.wav
   ```

10. Choisissez `4`, sélectionnez `odile`, puis enregistrez le préréglage que vous préférez. `Stable` et `Expressif` remplacent les trois réglages correspondants dans `profiles.json`; `Neutre` conserve les valeurs déjà présentes.

Votre voix apparaîtra ensuite parmi les profils dans le menu de doublage principal. Vous pourrez également sélectionner directement son WAV comme référence ponctuelle.

## Lister et supprimer

L'option **Lister les profils** affiche l'identifiant, la référence WAV et les réglages neutres enregistrés.

L'option **Supprimer un profil** retire uniquement son entrée de `voices/profiles.json` après confirmation. Le WAV correspondant est volontairement conservé dans `voices`, afin d'éviter toute suppression accidentelle et de permettre de recréer le profil plus tard.

Chaque ajout, suppression ou changement de préréglage conserve auparavant une archive complète de `profiles.json`. Pour revenir à une version antérieure, fermez l'utilitaire puis remplacez `voices/profiles.json` par l'archive horodatée désirée.
