# Talvora 2.0 - yerel Windows MCP

Aktif gelistirme `talvora-2/foundation` dalinin `v2/` klasorundedir.
Kokteki eski `.bat`, `src/` ve broker belgeleri onceki surume aittir; v2 kurulumu bunlari kullanmaz.

## Yerel kurulum

Daha once derlenmis ve alti araci test edilmis Windows kurulumunda:

```text
v2\TALVORA-KUR.cmd
```

Bu giris noktasi Windows UAC iznini kendisi ister; hazir Gateway dosyalarini kaynak/derleme klasorunden ayri bir calisma klasorune kopyalar. `Talvora Local MCP` gorevi kullanici oturum acilisinda en yuksek kullanici yetkisiyle baslar. Calistirma gunlukleri ve istemci ayari yedekleri tutulur.

- MCP: `http://127.0.0.1:7676/mcp`
- Health: `http://127.0.0.1:7676/healthz`
- Durum: `v2\TALVORA-DURUM.cmd`
- Yerel istemci kaydi: `~/.codex/config.toml` icinde `mcp_servers.talvora_local`

Talvora kurulumu API hesabi, API anahtari, OpenAI Tunnel veya public endpoint istemez. Yerel ChatGPT/Codex istemcisi MCP'ye dogrudan baglanir. Tarayicidaki sohbet yerel bilgisayara bu kayitla otomatik erisim kazanmaz. Istemcinin model oturumu ayri bir konudur; ChatGPT hesabi ile giris kullanilabilir.

## Mevcut araclar

`talvora_read_text`, `talvora_write_text`, `talvora_delete`, `talvora_list`, `talvora_run_process`, `talvora_system_info`.

Cekirdek Windows'ta gercek Unicode dosya yaz/oku/listele/sil ve process cikti testinden gecmistir. Yeni kurulum da ayni smoke testini calistirir. Yeni kurulumun kullanicinin bilgisayarinda uygulanmasi ve yerel istemci baglantisi ayri dogrulama adimlaridir.

## Temel kararlar

Tam Windows yetenekleri; yapay dosya sandbox'i veya komut deny-list'i yok. Paket yonetimi Chocolatey-first; WinGet yok. Bu kurulum Administrator baglamini kullanir; ayri SYSTEM broker'i v2'de henuz hazir degildir. Windows'un ve istemcinin gercek yetki gereksinimleri capability olarak gizlenmez.

Yerel kurulum ayrintilari: `v2/LOCAL-SETUP.md`.
