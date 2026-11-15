<p align="right">
  <a href="FRAMEWORK.md">English</a> | <b>Français</b>
</p>

# FileForge — Spécification & Documentation IA du Framework

Ce document est le manifeste technique destiné à l'agent IA et aux développeurs. Il récapitule l'ensemble des capacités, composants et contrats publics exposés par **FileForge**.

---

## 🎯 Périmètre & Responsabilité

FileForge est la bibliothèque partagée d'ingénierie système pour Windows dédiée aux **opérations d'E/S haute performance**, à la **synchronisation de répertoires**, à la **résolution de volumes physiques/réseau** et au **rendu de tableaux de bord en mode console**.

---

## 🧱 Composants & Capacités Détaillées

### 1. `FileForge.IO` — Transfert & Catalogue
- **`FileCopier`** (statique) :
  - `CopyFile(source, destination, bufferSize, displayProgress, verifyHash, cancellationToken)` : Copie par flux direct avec gestion coopérative de l'annulation et vérification optionnelle SHA-256.
  - `CompareFileHashes(file1, file2)` : Vérification d'intégrité par hachage cryptographique SHA-256 entre deux fichiers.
  - `ComputeFileHash(filePath, algorithm)` / `ComputeFileHashAsync(...)` : Calcul synchrone et asynchrone de l'empreinte hexadécimale d'un fichier physique (SHA-256 par défaut).
  - `ComputeHash(stream, algorithm)` / `ComputeHashAsync(...)` : Calcul de l'empreinte hexadécimale à partir d'un flux source `Stream`.
  - `VerifyFileHash(filePath, expectedHashHex, algorithm)` : Vérification d'intégrité d'un fichier par rapport à une empreinte hexadécimale attendue.
  - `ReadFileHeader(filePath, byteCount)` / `ReadFileHeaderAsync(...)` : Lecture non bloquante des $N$ premiers octets d'un fichier pour détection d'en-tête et signatures magiques.
  - `MatchesFileHeader(filePath, expectedHeader)` : Validation de la présence d'une signature binaire au début d'un fichier physique.
  - `CalculateOptimalBufferSize(fileSize)` : Calcul dynamique de la taille de buffer (de 64 Ko à 16 Mo) selon le volume du fichier.
  - `CopyMetadata(source, destination, includeAcl)` : Préservation des dates de création, modification, accès et attributs.
  - `CopyAcl(source, destination)` : Réplication des listes de contrôle d'accès de sécurité Windows.
- **`SyncCatalogRepository`** (statique) :
  - `LoadCatalog(catalogFilePath)` : Charge l'index binaire des fichiers sauvegardés en `Dictionary<string, string>`.
  - `SaveCatalog(catalog, catalogFilePath)` : Enregistre de façon atomique le catalogue de sauvegarde.
  - `MigrateLegacyBackupNames(destinationRoot)` : Renomme les dossiers d'anciennes versions en `Backup_YYYYMMDD_HHMM`.
  - `BootstrapInitialCatalog(sourcePath, destinationPath)` : Initialise un index rétroactif à partir d'une sauvegarde existante.
- **`FileOperationProgress`** : Modèle de progression d'opération de fichier (pourcentage, octets transférés, état d'achèvement).
- **`PersistentDelimitedFileWriter`** :
  - `PersistentDelimitedFileWriter(filePath, headerLine)` / `PersistentDelimitedFileWriter(filePath, headerLine, encoding)` : le fichier est écrit et lu en UTF-8 avec marque d'ordre d'octets, sauf encodage fourni ; passer `new UTF8Encoding(false)` pour du NDJSON, que certains lecteurs JSON Lines refusent quand il commence par une marque d'ordre d'octets.
  - `InitializeAsync(cancellationToken)` : Initialise un fichier délimité (CSV, TSV) en mode partagé non-bloquant (`FileShare.ReadWrite`), écrit la ligne d'en-tête si le fichier est nouvellement créé, ou positionne le curseur en fin de flux.
  - `ReadExistingLinesAsync(cancellationToken)` : Lit de manière asynchrone l'ensemble des enregistrements préexistants (hors en-tête) pour hydrater les registres de dédoublonnage au démarrage. Avec une ligne d'en-tête vide ou blanche (NDJSON), aucun en-tête n'est écrit ni sauté : toutes les lignes non vides sont renvoyées.
  - `AppendLineAsync(line, cancellationToken)` : Ajoute une ligne au fil de l'eau avec `AutoFlush` et synchronisation thread-safe (`SemaphoreSlim`).
  - `EscapeField(value, delimiter)` : Échappement conforme RFC 4180 (prise en charge des virgules, retours à la ligne et guillemets doublés).
  - `EscapeField(value, delimiter, neutralizeFormulas)` : même échappement ; quand `neutralizeFormulas` vaut `true`, une valeur qui commence par `=`, `+`, `-`, `@`, une tabulation, un retour chariot ou les formes pleine chasse `＝`, `＋`, `－`, `＠` est précédée d'une apostrophe, de sorte qu'un tableur qui ouvre le fichier l'affiche comme du texte, apostrophe comprise, au lieu de l'évaluer (injection de formule CSV). Un nombre négatif comme `-5` est préfixé lui aussi, et l'apostrophe fait partie de la valeur relue par `DelimitedRecordReader`.
- **`DelimitedRecordReader`** (statique, `FileForge.Core`) :
  - `ReadRecordsAsync(reader, delimiter, cancellationToken)` : Lit un texte délimité (CSV, TSV) enregistrement par enregistrement selon la RFC 4180, sous forme de `DelimitedRecord(LineNumber, Fields)`. Un champ entre guillemets peut contenir des séparateurs, des retours à la ligne et des guillemets doublés : un enregistrement écrit par `EscapeField` est relu entier. `LineNumber` est la ligne physique où commence l'enregistrement. Les enregistrements se terminent par CRLF, LF ou CR seul ; les lignes vides sont sautées ; un guillemet au milieu d'un champ sans guillemets est gardé comme texte. Un champ entre guillemets encore ouvert à la fin du texte lève `InvalidDataException`, qui nomme la ligne où il s'ouvre.
  - `ReadRecords(reader, delimiter)` : la même lecture, synchrone, pour un appelant lui-même synchrone qui devrait sinon bloquer sur `ReadRecordsAsync`. Les enregistrements sont lus au fil de l'énumération ; les erreurs d'arguments sont levées à l'appel, `InvalidDataException` pendant l'énumération.
- **`AtomicFileWriter`** (statique, `FileForge.Core`) : remplace le contenu d'un fichier de sorte qu'un lecteur, un plantage ou une coupure de courant trouve l'ancien contenu ou le nouveau, jamais un mélange.
  - `WriteAsync(path, writeContent, cancellationToken)` / `WriteAllTextAsync(path, contents, encoding, cancellationToken)` / `WriteAllText(path, contents, encoding)` : tient `<fichier>.lock` (ouvert sans partage ; supprimé après libération sous Windows, gardé sous Linux et macOS où le verrou est consultatif) pendant toute l'écriture, de sorte qu'une seconde écriture ou une récupération du même fichier reçoive entre-temps une `IOException` ; écrit `<fichier>.tmp`, le vide sur disque, le renomme `<fichier>.pending`, puis remplace le fichier par `File.Replace`, qui garde la liste de contrôle d'accès et les attributs sous Windows ; ailleurs, le nouveau contenu est créé avec les droits du fichier. Un lien symbolique est suivi et conservé. Le texte est écrit en UTF-8 sans marque d'ordre d'octets, sauf encodage fourni. Quand le fichier ne peut pas être remplacé (sous Windows, tant qu'un autre processus le tient ouvert), une `IOException` nomme `<fichier>.pending`, qui garde le nouveau contenu.
  - `RecoverInterruptedWrite(path)` : à appeler avant de lire le fichier après un plantage ; un contenu `.pending` remplace le fichier, un contenu `.tmp` (incomplet) est supprimé ; renvoie `InterruptedWriteRecovery(PendingContentApplied, IncompleteContentDiscarded)`.
  - Limites : le dossier du fichier doit être inscriptible ; un fichier monté seul (montage d'un fichier dans un conteneur) ne peut pas être remplacé par renommage ; un lien dur n'est pas conservé ; dans le cas rare où Windows retire le fichier sans pouvoir renommer le nouveau contenu, le fichier manque jusqu'à la prochaine écriture ou récupération.
- **`ProtectedFileStore`** (livré dans le package portable `FileForge.Core`, `net8.0`, utilisable par les applications qui ne ciblent pas Windows) :
  - `ReadSecret()` / `WriteSecret(secret)` / `ClearSecret()` : Persistance locale sécurisée de secrets sur fichier chiffré (DPAPI `ProtectedData` en portée `CurrentUser` sous Windows, AES-256 avec IV préfixé en repli).
  - `EncryptAesWithEntropy(plainBytes, entropy)` / `DecryptAesWithEntropy(cipherData, entropy)` : Primitives de chiffrement symétrique AES-256 liées à une entropie applicative.
  - Implémente aussi `ISecretStore` (`ReadSecretAsync` / `WriteSecretAsync` / `ClearSecretAsync`, tous avec un `CancellationToken`). Hors Windows, la clé AES provient de l'entropie, publique si elle reste à sa valeur par défaut : le fichier n'est alors qu'obscurci ; préférer `SecretServiceSecretStore` sous Linux.
- **`ISecretStore`** : contrat commun pour lire, écrire et supprimer un petit secret de façon asynchrone ; permet à une application de choisir un magasin selon la plateforme. Les échecs lèvent `SecretStoreException` (le message nomme l'opération, jamais le secret) ; `SecretStoreUnavailableException` signale que le magasin est inutilisable sur cette machine.
- **`SecretServiceSecretStore`** (Linux, Secret Service freedesktop via la commande `secret-tool`, paquet Debian/Ubuntu `libsecret-tools`) :
  - `new SecretServiceSecretStore(label, attributes)` : l'élément est identifié par des attributs fournis par l'appelant (par exemple `application` et `key`) ; délai par défaut de 10 s par commande.
  - `IsAvailableAsync(cancellationToken)` : `true` quand `secret-tool` est dans le PATH et qu'un trousseau répond ; `false` quand la commande manque, qu'aucune session D-Bus ne répond ou que le délai expire. À appeler avant de choisir le magasin.
  - `ReadSecretAsync` (`null` si absent) / `WriteSecretAsync` / `ClearSecretAsync` : le secret passe par l'entrée standard, les attributs sont des arguments séparés, rien ne passe par un shell.
- En interne, le magasin lance `secret-tool` par un exécuteur de processus interne (sans shell, `ArgumentList`, arbre de processus tué au dépassement du délai ou à l'annulation) ; il ne fait pas partie de l'API publique.

### 2. `FileForge.Sync` — Moteur de Synchronisation
- **`DirectorySynchronizer`** (statique) :
  - `CloneDirectory(source, destination, options, cancellationToken)` : Synchronisation miroir exacte 1:1 avec suppression des fichiers orphelins cibles.
  - `IncrementalBackup(source, destination, options, cancellationToken)` : Sauvegarde incrémentale créant un dossier daté `Backup_...` basé sur le catalogue.
  - `ParallelEnumerateFiles(directory, maxParallelism, cancellationToken)` : Découverte parallèle haute vitesse des fichiers et sous-répertoires.
  - `ForceDeleteDirectory(path)` : Suppression récursive robuste supprimant préalablement les attributs ReadOnly sur les fichiers et dossiers protégés.
- **`DirectorySyncOptions` & `DirectoryCloneOptions`** :
  - Gestion du parallélisme distinct pour la source, la destination et les transferts.
  - Chemins et masques exclus (`ExcludedPaths`, `ExcludedFilters`, `IncludedFilters`).

### 3. `FileForge.Storage` — Disques & Recommandations Matérielles
- **`VolumeIdentityResolver`** (statique) :
  - `GetDiskId(path)` : Extrait le numéro de série de volume Windows (`VOL:XXXXXXXX`) ou la racine réseau normalisée (`UNC:\\serveur\partage`).
  - `AreMatchingDiskIds(diskId1, diskId2)` : Comparaison insensible aux variations de casse ou de barres obliques.
  - `NormalizeDiskId(diskId)` : Harmonisation des formats de préfixes.
- **`HardwarePerformanceAdvisor`** (statique) :
  - `IsSsdDrive(path)` : Détecte si le lecteur sous-jacent est un SSD ou un disque magnétique traditionnel.
  - `CalculateOptimalParallelism(source, dest)` : Calcule le nombre idéal de threads de copie selon le type de stockage (SSD vs HDD) et le nombre de cœurs CPU.
  - `GetAvailableMemoryInGB()` : Interroge la mémoire vive physique disponible.

### 4. `FileForge.Paths` — Gestion des Chemins
- **`PathNormalizer`** (statique) :
  - `NormalizeLongPath(path)` : Préfixe les chemins longs avec `\\?\` ou `\\?\UNC\` pour dépasser la limite Windows MAX_PATH (260 caractères).
  - `RemoveLongPathPrefix(path)` : Supprime les préfixes techniques pour l'affichage utilisateur.

### 5. `FileForge.Terminal` — Rendu Console Rétro
- **`ConsoleDashboard`** :
  - Affiche un cadre double bordure (style Norton Commander) rafraîchi en temps réel sans scintillement.
  - Phases supportées : *Scanning* (inventaire), *UpToDate* (déjà à jour), *Copying* (transfert), *Completed* (terminé).
  - Barre de progression textuelle `[██████████░░░░░░░░░░]`.
  - Statistiques en temps réel : débit instantané, fichiers restants, temps écoulé / restant estimé.

---

## 📋 Protocole de Maintenance par l'Agent IA

Toute modification de FileForge doit respecter :
1. **Rétrocompatibilité :** Conserver toutes les méthodes publiques existantes. Utiliser des surcharges ou des paramètres optionnels.
2. **Qualité :** Zéro warning (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zéro pragma/suppress, `CancellationToken` systématique sur les I/O.
3. **Tests :** Nommage `MethodeCible_Scenario_ResultatAttendu`.
4. **Mise à jour :** Compléter immédiatement ce fichier `FRAMEWORK.md` en cas d'ajout d'une nouvelle capacité.
5. **Validation :** Valider par `./verify.ps1` (Quality Gate: PASSED).
