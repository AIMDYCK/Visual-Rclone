# Rclone Commander Advanced — App Icon Generation Prompt

Prompt optimizado para modelos de difusión / generación de imágenes
(Midjourney, DALL·E 3, Stable Diffusion XL, Flux, Ideogram, etc.).

---

## PROMPT (English — primary)

```
App icon for a modern network storage control panel called "Rclone Commander Advanced".
Square 1:1 composition, perfectly centered, rounded-square (squircle) container with soft
Fluent / macOS Big Sur style corners, generous inner padding, isolated on a flat dark
background for easy cut-out and .ico conversion.

CONCEPT: A sleek modern cloud storage node seamlessly interwoven with mounted disk drives
and high-speed network nodes. A stylized volumetric cloud sits at the top center, and from
its base emerge three glowing data conduits that connect downward into three stacked
3D disk platters / drive cylinders arranged in a subtle isometric perspective. Thin
luminous circuit traces and small orbiting data packets wrap around the cloud, suggesting
fast encrypted sync. A faint hexagonal mesh grid glows behind the composition, hinting at
network topology and modular architecture.

PALETTE: Deep slate and charcoal background (#0D1117 to #1C2128 gradient), primary accent
in electric blue (#58A6FF), secondary accent in emerald green (#3FB950) for the "connected /
mounted" drive, and neon cyan (#39D0D8) rim-light highlights. Subtle inner glow, soft
ambient occlusion, crisp specular edges.

STYLE: Clean 3D render, minimalist modern vector / claymorphic hybrid, soft studio lighting,
gentle depth of field, high detail on the cloud and disk edges, no noise, no grain.
Absolutely NO text, NO letters, NO numbers, NO typography, NO watermark, NO logo wordmark.
Flat solid dark background, no scenery, no environment, no floor shadow beyond a soft
contact shadow directly under the icon.

OUTPUT: 1024x1024, ultra sharp, high contrast, centered, ready for multi-resolution .ico
export (16, 24, 32, 48, 64, 128, 256 px).
```

---

## PROMPT (variante — más "claymorphic / friendly")

```
Minimalist claymorphic 3D app icon, square, centered, rounded squircle shape.
A soft matte cloud merged with three glossy stacked disk drives, connected by glowing
cyan data streams. Dark charcoal background, electric blue and emerald green accents,
neon cyan rim light. Soft plastic / clay material, smooth rounded forms, gentle studio
lighting, subtle contact shadow. No text, no letters, no watermark. Flat dark isolated
background for easy cut-out. 1024x1024, ultra detailed, clean render.
```

---

## PROMPT (variante — más "tech / cyber")

```
Futuristic tech app icon, square, centered, rounded corners. A holographic cloud node
fused with isometric disk platters and glowing network nodes, thin neon circuit traces,
hexagonal grid backdrop. Dark slate background (#0D1117), electric blue (#58A6FF),
emerald green (#3FB950) and neon cyan highlights. Clean 3D render, sharp edges, volumetric
glow, no text, no typography, no watermark. Isolated flat dark background, 1024x1024.
```

---

## Negative prompt (para Stable Diffusion / Flux)

```
text, letters, words, numbers, typography, watermark, signature, logo wordmark, blurry,
low resolution, jpeg artifacts, noise, grain, cluttered, busy background, scenery,
landscape, people, hands, photorealistic photo, oversaturated, distorted geometry,
asymmetric, off-center, cropped, multiple icons
```

---

## Parámetros sugeridos

| Parámetro        | Valor recomendado                                   |
|------------------|-----------------------------------------------------|
| Aspect ratio     | `1:1` (cuadrado)                                    |
| Resolución       | `1024x1024` (o `2048x2048` para reescalar)          |
| Steps            | 30–50                                               |
| Guidance / CFG   | 6–8                                                 |
| Sampler          | DPM++ 2M Karras / Euler a                           |
| Style preset     | "3D render" / "Digital art" / "Minimalist"          |
| Seed             | Fijo para reproducibilidad                          |

---

## Post-proceso para generar el `.ico`

1. Genera la imagen a `1024x1024` con fondo oscuro plano.
2. Recorta el squircle si el modelo añadió margen extra (deja ~8 % de padding).
3. Exporta a `.ico` multi-resolución con los tamaños:
   `16, 24, 32, 48, 64, 128, 256`.
   - **ImageMagick**:
     ```
     magick icon.png -define icon:auto-resize=256,128,64,48,32,24,16 app.ico
     ```
   - **Online**: icoconvert.com / convertico.com
4. Coloca el archivo como `src/RcloneCommanderAdvanced/Assets/app.ico`.
5. Referéncialo en el `.csproj`:
   ```xml
   <ItemGroup>
     <Resource Include="Assets\app.ico" />
   </ItemGroup>
   <PropertyGroup>
     <ApplicationIcon>Assets\app.ico</ApplicationIcon>
   </PropertyGroup>
   ```
6. Para el icono de la ventana en XAML:
   ```xml
   <Window ... Icon="/Assets/app.ico">
   ```

---

## Paleta de referencia (a juego con la app)

| Rol                     | Hex       |
|-------------------------|-----------|
| Fondo profundo          | `#0D1117` |
| Fondo panel             | `#161B22` |
| Fondo tarjeta           | `#1C2128` |
| Borde                   | `#30363D` |
| Acento (azul eléctrico) | `#58A6FF` |
| Éxito (verde esmeralda) | `#3FB950` |
| Peligro (rojo)          | `#F85149` |
| Advertencia (ámbar)     | `#D29922` |
| Texto primario          | `#E6EDF3` |
| Texto secundario        | `#8B949E` |
| Cian neón (rim light)   | `#39D0D8` |
