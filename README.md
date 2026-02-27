# setups

Repositorio con configuración de despliegue automático: cada vez que subas cambios desde tu PC a GitHub, los archivos se actualizan automáticamente en el servidor web.

## ¿Cómo funciona?

1. Editas archivos en tu PC.
2. Haces `git add .`, `git commit -m "descripción"` y `git push`.
3. GitHub Actions detecta el push y despliega los archivos al servidor web vía FTP automáticamente.

## Configuración inicial

Antes de usar el despliegue automático, debes agregar los siguientes **Secrets** en tu repositorio de GitHub:

1. Ve a tu repositorio en GitHub → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.
2. Agrega los siguientes secrets:

| Secret           | Descripción                                                      | Ejemplo                  |
|------------------|------------------------------------------------------------------|--------------------------|
| `FTP_SERVER`     | Dirección del servidor FTP de tu hosting                         | `ftp.tudominio.com`      |
| `FTP_USERNAME`   | Usuario FTP                                                      | `usuario@tudominio.com`  |
| `FTP_PASSWORD`   | Contraseña FTP                                                   | `tu_contraseña_segura`   |
| `FTP_SERVER_DIR` | Carpeta de destino en el servidor (normalmente `/public_html/`) | `/public_html/`          |

## Uso diario

```bash
# 1. Realiza tus cambios en los archivos del proyecto

# 2. Agrega los cambios al staging
git add .

# 3. Haz commit con un mensaje descriptivo
git commit -m "Actualización de archivos"

# 4. Sube los cambios a GitHub (esto activa el despliegue automático)
git push
```

Después del `git push`, puedes ver el progreso del despliegue en la pestaña **Actions** de tu repositorio en GitHub.
