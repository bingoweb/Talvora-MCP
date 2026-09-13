# Talvora Core Design

Talvora yalnız Windows 11'i hedefleyen, ChatGPT ile yerel Windows sistemi arasında MCP tabanlı bir köprüdür. Çekirdek .NET 10 LTS üzerinde modüler monolit olarak tasarlanır. MCP bir dış adapterdır; iş mantığı MCP tiplerine bağımlı değildir. Yönetici bağlamı ayrı Elevated Broker process'inde tutulur. Başlangıç kabiliyetleri FileSystem, Shell, Processes ve System Info'dur.

Kalıcı tasarım kararları ve bağımlılık kuralları `docs/architecture/Talvora-Core-v1.md` dosyasındadır.
