# 💎 PRISMA — Plataforma de Reseñas y Síntesis Inteligente

**Prisma** es una aplicación web moderna diseñada para registrar lugares, compartir reseñas con calificaciones en estrellas, almacenar toda la información de manera persistente en una base de datos SQLite y condensar todas las opiniones de cada lugar en un único párrafo mediante una API de inteligencia artificial / síntesis analítica.

---

## 🚀 Inicio Rápido

### 1. Iniciar la aplicación
Abre una terminal en esta carpeta y ejecuta:
```bash
dotnet run --urls "http://localhost:5120"
```

### 2. Abrir en el navegador
Ingresa a:
```
http://localhost:5120
```

---

## 🌟 Características Principales

1. **Catálogo de Lugares**:
   - Visualización con imágenes, categorías, calificaciones promedio y recuento de reseñas.
   - Búsqueda en tiempo real por nombre, dirección o términos clave.
   - Filtro interactivo por categorías (Cafés, Gastronomía, Cultura, Naturaleza, Ocio).

2. **Registro de Nuevos Lugares**:
   - Formulario para que cualquier usuario agregue nuevos espacios con categoría, dirección, imagen y descripción.
   - Plantillas de imágenes listas para usar o posibilidad de ingresar URLs personalizadas.

3. **Publicación de Reseñas**:
   - Selector interactivo de 1 a 5 estrellas con etiquetas de satisfacción.
   - Autor, título y comentario detallado de la experiencia.
   - Almacenamiento seguro en base de datos SQLite local (`prisma.db`).

4. **API de Síntesis / Resumen de Reseñas**:
   - La API (`/api/places/{id}/summarize`) recolecta **todas las reseñas existentes en la base de datos** para ese lugar.
   - Genera un **único párrafo conciso, fluido y elocuente** que destaca fortalezas, advertencias o consejos útiles y el veredicto general.
   - **Soporte Híbrido**:
     - **Motor Analítico Local**: Funciona de inmediato sin necesidad de internet ni claves API.
     - **Google Gemini 1.5 Flash**: Puedes configurar opcionalmente tu `GEMINI_API_KEY` en el botón de ajustes en la barra superior o en `appsettings.json` para síntesis con LLM en tiempo real.

---

## 🛠️ Arquitectura y Endpoints REST

- `GET /api/places` — Lista todos los lugares con métricas calculadas (rating promedio, total de reseñas, resumen actual).
- `GET /api/places/{id}` — Detalle completo del lugar con su historial de reseñas y resumen.
- `POST /api/places` — Registra un nuevo lugar.
- `POST /api/places/{id}/reviews` — Publica una nueva reseña y recalcula automáticamente la síntesis.
- `POST /api/places/{id}/summarize` — Recopila todas las reseñas de la BD y regenera el párrafo resumen.
- `GET /api/stats` — Estadísticas globales (total lugares, total reseñas, promedio global).
