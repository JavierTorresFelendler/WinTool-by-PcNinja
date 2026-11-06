# Instructions for AI assistants

Read before changing anything in this repository.

1. **Decisions are binding.** `docs/DECISIONS.md` lists accepted decisions.
   Do not contradict them. To change one, propose a new entry and ask the
   owner.
2. **Open questions belong to the owner.** Do not resolve a `Pending` item in
   `docs/DECISIONS.md` on your own.
3. **Never push to the source repositories** (WinUpdate Tool V1/V2, Windows
   Upgrade Tool, Smart Office Installer). To carry over a change from them,
   follow `docs/UPSTREAM-SYNC.md`.
4. **Runtime and UI:** .NET Framework 4.8, x64, WPF with MVVM. Feature modules
   never reference WPF and never reference each other.
5. **No user-visible text in code or XAML.** Use the `.resx` resources
   (`docs/LOCALIZATION.md`).
6. **Machine-changing operations** run under the operation lock in
   `WinTool.Core`.
7. **`shared/` files are byte-identical** with the other PcNinja tools. Do not
   edit them here alone; see `shared/README.md`.
8. Documentation and code comments are in English. Conversation with the owner
   may be in Hebrew.
