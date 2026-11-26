# FlashCopy Pro

Copiador de archivos gratuito para Windows 11 x64 (C# / WinForms, .NET Framework 4.8).

## Funciones
- Copiar, mover, comprobar (hash) y eliminar, con verificación CRC32/MD5/SHA1/SHA256/SHA512.
- Sustituye Ctrl+V en el Explorador cuando hay archivos copiados o cortados.
- Menú del clic derecho, "Enviar a", Ctrl+Mayús+V.
- Reordenar la lista (arrastrar, Ctrl+↑/↓, Ctrl+Inicio/Fin) y quitar elementos (Supr).
- Progreso y porcentaje en el icono de la barra de tareas y de la bandeja.
- Pausa, detener y reanudar desde donde se quedó (incluido el archivo a medias).

## Compilar
Requisitos: .NET SDK 6 o superior (compila para net48, incluido en Windows 11) y NSIS para el instalador.

```
cd FlashCopy
dotnet build -c Release -o out
```
Para el instalador, desde la carpeta raíz ejecuta:
```
makensis installer.nsi
```

## Archivos
- `Engine.cs` – motor de copia, hashes, reanudación.
- `MainForm.cs` – ventana principal y lista de archivos.
- `Integration.cs` – barra de tareas y sustitución de Ctrl+V.
- `Shell.cs` – registro en el Explorador y ajustes.
- `Dialogs.cs`, `Controls.cs` – diálogos y controles con tema oscuro.
- `Program.cs` – arranque e instancia única.
