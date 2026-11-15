<p align="right">
  <b>English</b> | <a href="FRAMEWORK.fr.md">Français</a>
</p>

# FileForge — Framework Specification & AI Documentation

This document is the technical manifest intended for AI agents and software engineers. It outlines all capabilities, architectural components, and public contracts exposed by **FileForge**.

---

## 🎯 Scope & Responsibility

FileForge is a shared Windows system engineering library dedicated to **high-performance streaming I/O**, **directory synchronization**, **physical and network volume resolution**, and **retro console dashboards**.

---

## 🧱 Components & Detailed Capabilities

### 1. `FileForge.IO` — Streaming Transfers, Integrity & Catalogs
- **`FileCopier`** (static):
  - `CopyFile(source, destination, bufferSize, displayProgress, verifyHash, cancellationToken)`: Direct stream-based file copy with cooperative cancellation and optional SHA-256 integrity verification.
  - `CompareFileHashes(file1, file2)`: Cryptographic SHA-256 integrity check between two files.
  - `ComputeFileHash(filePath, algorithm)` / `ComputeFileHashAsync(...)`: Synchronous and asynchronous hexadecimal file hashing (SHA-256 by default).
  - `ComputeHash(stream, algorithm)` / `ComputeHashAsync(...)`: Hexadecimal hash generation from a `Stream`.
  - `VerifyFileHash(filePath, expectedHashHex, algorithm)`: File integrity verification against an expected hexadecimal digest.
  - `ReadFileHeader(filePath, byteCount)` / `ReadFileHeaderAsync(...)`: Non-blocking read of the first $N$ bytes for magic number header detection.
  - `MatchesFileHeader(filePath, expectedHeader)`: Validates whether a file begins with an expected binary header sequence.
  - `CalculateOptimalBufferSize(fileSize)`: Dynamic buffer size calculation (from 64 KB up to 16 MB) proportional to file size.
  - `CopyMetadata(source, destination, includeAcl)`: Preserves creation, modification, and access timestamps along with file attributes.
  - `CopyAcl(source, destination)`: Replicates Windows NTFS Access Control Lists (ACLs).
- **`SyncCatalogRepository`** (static):
  - `LoadCatalog(catalogFilePath)`: Loads the binary backup catalog into a `Dictionary<string, string>`.
  - `SaveCatalog(catalog, catalogFilePath)`: Atomically persists the backup index catalog.
  - `MigrateLegacyBackupNames(destinationRoot)`: Renames legacy backup folders to `Backup_YYYYMMDD_HHMM`.
  - `BootstrapInitialCatalog(sourcePath, destinationPath)`: Initializes a retroactive catalog from existing backup targets.
- **`FileOperationProgress`**: File operation progress model (percentage, bytes transferred, completion state).
- **`PersistentDelimitedFileWriter`**:
  - `PersistentDelimitedFileWriter(filePath, headerLine)` / `PersistentDelimitedFileWriter(filePath, headerLine, encoding)`: the file is written and read in UTF-8 with a byte order mark unless an encoding is given; pass `new UTF8Encoding(false)` for NDJSON, which some JSON Lines readers reject when it starts with a byte order mark.
  - `InitializeAsync(cancellationToken)`: Initializes a delimited file (CSV, TSV) in non-blocking shared mode (`FileShare.ReadWrite`), writes headers on creation, or positions the pointer at the end of stream.
  - `ReadExistingLinesAsync(cancellationToken)`: Asynchronously reads existing non-header lines to hydrate deduplication caches on startup. With an empty or white-space header line (NDJSON), no header is written or skipped, so every non-empty line is returned.
  - `AppendLineAsync(line, cancellationToken)`: Thread-safe streaming line appender with `AutoFlush` and `SemaphoreSlim` synchronization.
  - `EscapeField(value, delimiter)`: RFC 4180-compliant field escaping (handling commas, line breaks, and escaped double quotes).
  - `EscapeField(value, delimiter, neutralizeFormulas)`: same escaping; when `neutralizeFormulas` is `true`, a value starting with `=`, `+`, `-`, `@`, a tab, a carriage return or the full-width `＝`, `＋`, `－`, `＠` is prefixed with an apostrophe, so that a spreadsheet opening the file shows it as text, apostrophe included, instead of evaluating it (CSV formula injection). A negative number such as `-5` is prefixed too, and the apostrophe is part of the value read back by `DelimitedRecordReader`.
- **`DelimitedRecordReader`** (static, `FileForge.Core`):
  - `ReadRecordsAsync(reader, delimiter, cancellationToken)`: Reads a delimited text (CSV, TSV) record by record according to RFC 4180, as `DelimitedRecord(LineNumber, Fields)`. A quoted field may hold delimiters, line breaks and doubled quotes, so a record written by `EscapeField` is read back whole. `LineNumber` is the physical line on which the record starts. Records end with CRLF, LF or a lone CR; empty lines are skipped; a quote inside an unquoted field is kept as text. A quoted field still open at the end of the text throws `InvalidDataException` naming the line where it opens.
  - `ReadRecords(reader, delimiter)`: the same reading, synchronous, for callers that are synchronous themselves and would otherwise block on `ReadRecordsAsync`. Records are read as the sequence is enumerated; argument errors are thrown at the call, `InvalidDataException` during enumeration.
- **`AtomicFileWriter`** (static, `FileForge.Core`): replaces the content of a file so that a reader, a crash or a power cut finds either the previous content or the new one, never a mix.
  - `WriteAsync(path, writeContent, cancellationToken)` / `WriteAllTextAsync(path, contents, encoding, cancellationToken)` / `WriteAllText(path, contents, encoding)`: holds `<file>.lock` (opened without sharing; deleted once released on Windows, kept on Linux and macOS where the lock is advisory) for the whole write, so that a second write or a recovery of the same file meanwhile gets an `IOException`; writes `<file>.tmp`, flushes it to disk, renames it `<file>.pending`, then replaces the file with `File.Replace`, which keeps the Windows access control list and attributes; elsewhere the new content is created with the permissions of the file. A symbolic link is followed and kept. Text is written in UTF-8 without byte order mark unless an encoding is given. When the file cannot be replaced (on Windows, while another process holds it open), an `IOException` names `<file>.pending`, which keeps the new content.
  - `RecoverInterruptedWrite(path)`: to call before reading the file after a crash; a `.pending` content replaces the file, a `.tmp` content (incomplete) is deleted; returns `InterruptedWriteRecovery(PendingContentApplied, IncompleteContentDiscarded)`.
  - Limits: the directory of the file must be writable; a file mounted on its own (a container bind mount) cannot be replaced by renaming; a hard link is not kept; in the rare case where Windows removes the file but cannot rename the new content, the file is missing until the next write or recovery.
- **`ProtectedFileStore`** (shipped in the portable `FileForge.Core` package, `net8.0`, usable from applications that do not target Windows):
  - `ReadSecret()` / `WriteSecret(secret)` / `ClearSecret()`: Secure local secret storage on encrypted files (DPAPI `ProtectedData` scoped to `CurrentUser` on Windows, AES-256 with prefixed IV as fallback).
  - `EncryptAesWithEntropy(plainBytes, entropy)` / `DecryptAesWithEntropy(cipherData, entropy)`: Symmetric AES-256 encryption primitives bound to application-specific entropy.
  - Also implements `ISecretStore` (`ReadSecretAsync` / `WriteSecretAsync` / `ClearSecretAsync`, all taking a `CancellationToken`). Outside Windows the AES key comes from the entropy, which is public when left at its default: the file is only obfuscated, so prefer `SecretServiceSecretStore` on Linux.
- **`ISecretStore`**: common contract to read, write and delete one small secret asynchronously; lets an application pick a store per platform. Failures raise `SecretStoreException` (message names the operation, never the secret); `SecretStoreUnavailableException` means the store cannot be used on this machine.
- **`SecretServiceSecretStore`** (Linux, freedesktop Secret Service through the `secret-tool` command, Debian/Ubuntu package `libsecret-tools`):
  - `new SecretServiceSecretStore(label, attributes)`: the item is identified by caller-given attributes (for example `application` and `key`); default timeout 10 s per command.
  - `IsAvailableAsync(cancellationToken)`: `true` when `secret-tool` is on PATH and a keyring answers; `false` when the command is missing, no D-Bus session answers or the timeout expires. Call it before choosing the store.
  - `ReadSecretAsync` (`null` when absent) / `WriteSecretAsync` / `ClearSecretAsync`: the secret goes through standard input, attributes are separate arguments, nothing runs through a shell.
- Internally the store runs `secret-tool` through an internal process runner (no shell, `ArgumentList`, process tree killed on timeout or cancellation); it is not part of the public API.

### 2. `FileForge.Sync` — Directory Synchronization Engine
- **`DirectorySynchronizer`** (static):
  - `CloneDirectory(source, destination, options, cancellationToken)`: Exact 1:1 directory mirroring with target orphan pruning.
  - `IncrementalBackup(source, destination, options, cancellationToken)`: Incremental backup producing dated `Backup_...` folders based on the state catalog.
  - `ParallelEnumerateFiles(directory, maxParallelism, cancellationToken)`: High-speed parallel discovery of files and subdirectories.
  - `ForceDeleteDirectory(path)`: Robust recursive deletion automatically clearing ReadOnly attributes on protected files and folders beforehand.
- **`DirectorySyncOptions` & `DirectoryCloneOptions`**:
  - Independent parallelism configuration for source, destination, and transfer workers.
  - Exclusion paths and pattern filters (`ExcludedPaths`, `ExcludedFilters`, `IncludedFilters`).

### 3. `FileForge.Storage` — Physical Disks & Hardware Heuristics
- **`VolumeIdentityResolver`** (static):
  - `GetDiskId(path)`: Extracts the Windows volume serial number (`VOL:XXXXXXXX`) or normalized network root (`UNC:\\server\share`).
  - `AreMatchingDiskIds(diskId1, diskId2)`: Case- and slash-insensitive volume identifier comparison.
  - `NormalizeDiskId(diskId)`: Normalizes prefix representations across platforms.
- **`HardwarePerformanceAdvisor`** (static):
  - `IsSsdDrive(path)`: Detects whether the underlying drive is an SSD or a rotational magnetic HDD.
  - `CalculateOptimalParallelism(source, dest)`: Calculates optimal worker thread count based on storage media (SSD vs. HDD) and available CPU cores.
  - `GetAvailableMemoryInGB()`: Queries physical available system RAM.

### 4. `FileForge.Paths` — Path Management & Normalization
- **`PathNormalizer`** (static):
  - `NormalizeLongPath(path)`: Prefixes long paths with `\\?\` or `\\?\UNC\` to bypass the Windows 260-character `MAX_PATH` limit.
  - `RemoveLongPathPrefix(path)`: Strips technical prefixes for user-facing displays and logs.

### 5. `FileForge.Terminal` — Retro Console Dashboards
- **`ConsoleDashboard`**:
  - Displays a flicker-free double-border terminal frame (Norton Commander-inspired) refreshed in real-time.
  - Supported phases: *Scanning*, *UpToDate*, *Copying*, *Completed*.
  - Text progress bars: `[██████████░░░░░░░░░░]`.
  - Real-time statistics: instant transfer throughput, pending file counts, elapsed / estimated remaining time.

---

## 📋 Maintenance Protocol for AI Agents

All enhancements to FileForge must comply with:
1. **Backward Compatibility:** Preserve existing public signatures. Introduce overloads or optional parameters.
2. **Code Cleanliness:** Zero warnings (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`), zero pragmas/suppressions, systematic `CancellationToken` on all I/O methods.
3. **Testing Standards:** Test naming convention `TargetMethod_Scenario_ExpectedResult`.
4. **Documentation:** Update both `FRAMEWORK.md` and `FRAMEWORK.fr.md` immediately upon introducing or modifying capabilities.
5. **Validation:** Full pass of `./verify.ps1` (SonarQube Quality Gate: PASSED).
