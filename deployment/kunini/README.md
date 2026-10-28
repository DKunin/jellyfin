# Развёртывание Jellyfin на Kunini

Jellyfin `v12.4` работает в контейнере с закреплённым digest и слушает только `127.0.0.1:8096`. Nginx публикует `https://media.kunini.ru`, проверяет каждое соединение через `auth.kunini.ru` и поддерживает WebSocket и длинные медиапотоки.

Плагин `Kunini Auth Bridge` убирает второй экран входа в веб-плеере. Проверенные Nginx заголовки `X-Kunini-Auth-*` преобразуются в отдельную Jellyfin-сессию `kunini-<username>-<stable-id>`. Смена пользователя Kunini приводит к смене Jellyfin-сессии до показа интерфейса. Новый пользователь `dkunin` получает права администратора по явному allowlist; остальные новые пользователи получают обычные права просмотра. Исходная локальная учётная запись `dkunin` и её пароль сохраняются для аварийного доступа через loopback.

Медиатека не копируется и не подключается через WebDAV. Каталог `/mnt/data/plex` передаётся контейнеру только для чтения как `/media`; создаются библиотеки:

- `Movies` → `/media/movies`;
- `Series` → `/media/series`;
- `Videos` → `/media/videos`.

Конфигурация и база находятся в `/var/apps/jellyfin/config`, а кэш и транскодирование — в `/mnt/data/.jellyfin-cache`. Для AMD APU включён VA-API через `/dev/dri/renderD128`.

## Развёртывание

```bash
./deployment/kunini/deploy.sh
ssh dekunin@192.168.28.3 /var/apps/jellyfin/deployment/initialize.sh
ssh dekunin@192.168.28.3 /var/apps/jellyfin/deployment/setup-nginx.sh
ssh dekunin@192.168.28.3 /var/apps/jellyfin/deployment/verify.sh
```

Пароль администратора `dkunin` создаётся один раз и хранится с правами `0600` в `/var/apps/jellyfin/secrets/admin-password`. Не добавляйте его в Git или журналы.

`deploy.sh` собирает bridge воспроизводимо в закреплённом .NET SDK-контейнере, выполняет его тесты, монтирует DLL в Jellyfin только для чтения, оставляя служебный `meta.json` доступным Jellyfin для записи, и применяет Nginx-конфигурацию только после успешной проверки внутреннего endpoint. Автовход относится к Jellyfin Web; нативные клиенты по-прежнему используют собственный Jellyfin login flow.

## Обновление и откат

Перед обновлением измените tag и digest образа в `compose.yaml`, затем снова запустите `deploy.sh`. Если контейнер уже существует, его образ получает локальный тег `jellyfin/rollback:<UTC>`; последний тег записывается в `/var/apps/jellyfin/last-rollback-image`.

Для отката удалите из `compose.yaml` переменные `KUNINI_AUTH_*` и mount `/config/plugins/KuniniAuth`, восстановите предыдущий Nginx-конфиг без `sub_filter`, укажите сохранённый образ и выполните `docker compose -p jellyfin -f /var/apps/jellyfin/deployment/compose.yaml up -d`. Локальная учётная запись администратора остаётся пригодной для входа. Конфигурацию `/var/apps/jellyfin/config` перед обновлением следует архивировать отдельно.

## Проверка

`verify.sh` проверяет точный digest образа, здоровье контейнера, loopback-only порт, read-only медиатеку и bridge, fail-closed поведение заголовков, регистрацию auth provider, три пути библиотек, состояние сканирования, сертификат и анонимный редирект на Kunini Auth. Полноценный браузерный вход проверяется отдельно с действующей Kunini-сессией.
