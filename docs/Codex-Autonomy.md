# Talvora Codex: Esnek gelistirme modu

Talvora, Windows'ta oturum acmis kullanicinin Codex CLI kurulumunu ve giris
bilgilerini kullanir. Codex normal kullanici haklariyla calisir.

## Varsayilan

- Model: gpt-6.1-sol; dusunme: medium.
- sandbox=workspace-write: proje icinde kod yazar, test ve derleme yapar.
- approvalMode=automatic: Codex --approve-for-me otomatik onay incelemesi.
- networkAccess=true: workspace-write icin ag erisimi talep edilir.
- preserveSession=true: oturum gecmisi korunur; sessionId yanitta doner.
- allowNonGitWorkspace=true: Git deposu olmayan klasorlerde de calisir.
- Tamamlanan komut ve MCP araci sayilari ile son calisma ozeti sunulur.

## Bir goreve devam

Ilk cevabin sessionId degerini sonraki cagrida resumeSessionId olarak verin.
Ayni workingDirectory kullanin. preserveSession=true olmali.

## Ek klasor ve genis yetki

additionalWritableDirectories listesine mutlak dizinler yazin.
Bu parametre ilk oturumda gecerlidir, exec resume ile desteklenmez.

Yalnizca istege bagli tek bir gorev icin sandbox=danger-full-access ve
allowFullAccess=true secilebilir. Bu durumda Codex sandbox'i kalkar;
Windows kullanicisi SYSTEM olmaz. Varsayilan mod daha kontrolludur.

## Daraltma secenekleri

- sandbox=read-only: yalniz inceleme.
- approvalMode=never: otomatik onayi kapat.
- networkAccess=false: ag erisim istegini kapat.
- preserveSession=false: tek seferlik, kaydedilmeyen oturum.

## Henuz eksik

Uzun gorevlerde canli ilerleme ve iptal icin ayri endpoint bulunmuyor;
Codex tamamlaninca sonuc donuyor. Bazi Windows NuGet/cache dizinlerine
erisim, Codex sandbox'inda ek yazilabilir klasor gerektirebilir.
