# Carga Ops — Handoff para Claude Code

Aplicación web para administrar la carga laboral de Operaciones en tres unidades: **Procesamiento de Datos (DP)** y **Codificación** (ambas con planificación semanal) y **Scripting** (demanda ad-hoc). Texto original: **Procesamiento de Datos (DP)** con planificación semanal y **Scripting** con demanda ad-hoc. Funciona como un tablero tipo Kanban y tiene control de acceso por rol.

- Mockup interactivo de referencia: `Carga Ops.dc.html`. La clase `Component` implementa toda la lógica descrita aquí; úsala como especificación ejecutable. La versión anterior, sin usuarios, quedó en `Carga Ops v1.dc.html`.
- Planilla de origen: `uploads/Prog DP.xlsx`, hoja `agenda`. Muestra el proceso manual que se usa hoy, con un entregable por fila. Sus 49 filas únicas son los datos semilla del mockup.
- **Estilo gráfico:** `carga-ops.css`, basado en `uploads/site[1].css`, la guía corporativa (navy `#121b5a` + teal `#14a19e`, IBM Plex Sans/Mono, superficies blancas sobre `#f4f6fa`, radio 6px y modo oscuro por `prefers-color-scheme` o `data-theme`).
  - En producción usar `site.css` + Bootstrap 5, que es lo que esa hoja mapea. Las clases del mockup equivalen así:
    - `.btn-primary` = navy
    - `.btn-secondary` → `.btn-outline-secondary` (teal)
    - `.input` → `.form-control` / `.form-select`
    - `.card`, `.table` y badges `.be-*` = mismos nombres
  - **Shell:**
    - `.topbar` navy sticky: marca, "Nuevo requerimiento" (botón blanco, solo líder), avatar con nombre y rol, "Salir".
    - `.side`, navegación lateral de 232px con íconos Lucide: el ítem activo lleva fondo teal-50 y una barra teal a la izquierda; contador rojo `.cnt` de atrasados en Resumen y Mi semana.
    - Por debajo de 992px el lateral se oculta y aparece `.mobnav` con píldoras.
  - **Estados (`.be` con punto):**
    - Pendiente = neutral, En curso = info (teal), OK = success.
    - Capacidad: Equilibrado = success, Subutilizado = info, Sobrecargado = warning, Sobre capacidad = danger.
    - Disponibilidad de scripting: Siguiente = success, Al límite = danger.
    - Consulta pública: Por asignar = warning.
  - **Color semántico:** atrasado / vencido / error = `--danger`, aviso = `--warning`, OK = `--success`.
  - El kanban usa columnas `--neutral-50` con tarjetas blancas; al arrastrar sobre una columna se resalta en teal-50.
  - La versión con el sistema Industry anterior quedó en `Carga Ops v2 (Industry).dc.html`.
- Layout responsivo: botones, tags, opciones segmentadas, kickers, códigos de Job y fechas llevan `white-space: nowrap`. Las tablas anchas van dentro de un contenedor con `overflow-x: auto`.

> **Mantener este archivo sincronizado** con cada cambio de diseño o lógica en `Carga Ops.dc.html`.

---

## 1. Usuarios, roles y permisos

```ts
type Rol = 'lider' | 'analista' | 'comercial';
type Equipo = 'proc' | 'script' | 'cod';
interface Usuario { id: string; nombre: string; email: string; rol: Rol; equipos: Equipo[]; activo: boolean }
```

- **Un analista puede pertenecer a ambos equipos** (`equipos: ['proc','script']`) y tener entregables DP y scripts el mismo día o semana. Su vista "Mi semana" mezcla ambos tipos (columna Tipo: DP / Script). Participa en la planificación DP y en el ranking de scripting.
- En el mockup, Cristian Gómez y Luis Ruiz son mixtos. El diálogo de usuario usa checkboxes de equipo y exige al menos uno.

| Capacidad | Líder de equipo | Analista DP | Analista Scripting |
| --- | --- | --- | --- |
| Ver requerimientos | Todos (proc + scripts), y además "Mis asignaciones" con lo asignado a él | Solo los asignados a él | Solo los asignados a él |
| Tabs | Tablero · Planificación semanal · Scripts · Resumen · Histórico · Notificaciones · Usuarios | Mi tablero · Mi semana | Mi tablero · Mi semana |
| Descargar planilla | Semana completa | Su semana | Su semana |
| Crear, eliminar, asignar o cambiar fecha | ✓ | — | — |
| Importar planilla y balancear | ✓ | — | — |
| Cambiar estado (pendiente ↔ en curso) | ✓ | Solo los propios | Solo los propios |
| Dar OK de término | ✓ (cualquiera) | Solo los propios | Solo los propios |
| Reabrir un requerimiento con OK | ✓ | — | — |
| Administrar usuarios | ✓ | — | — |

- **Consulta sin cuenta** (personas de la organización que no participan de la gestión): enlace en el login → vista pública de solo lectura. Detalle en §3.5.
- **Login:** correo + contraseña. En el mockup la contraseña no se valida; los botones "Accesos de demostración" permiten entrar con cualquier usuario. En producción usar SSO o auth real y aplicar los permisos **en el backend**: el filtro "solo lo asignado" debe ir en la API, no solo en la UI.
- La sesión persiste en `localStorage['cargaops.session']`. "Salir" la cierra.
- **Single Sign-On (implementación):** colaboradores en la red interna o VPN ingresan con su cuenta de Windows / Active Directory (patrón de Ipsos.Bhe.Auth: Negotiate en `/cuenta/sso` y, con dos hosts, ticket HMAC-SHA256 de un solo uso).
  - Entran los usuarios registrados, aprobados y activos, mapeados por cuenta de dominio o `usuario@dominio`.
  - Una cuenta corporativa sin usuario deja una **solicitud de acceso** pendiente, con nombre y correo de AD.
  - Las cuentas de `Sso:Administradores` entran como líder la primera vez.
  - InPrivate o fallo → formulario de ingreso.
- **Acceso y cuentas (implementación, sin pantalla en el mockup):**
  - El ingreso acepta **correo o RUT**. Con clave incorrecta el mensaje no revela si la cuenta existe.
  - **Política de contraseñas:** 8+ caracteres, con mayúscula, minúscula, número y símbolo, y sin nombre ni correo.
  - **"Solicitar acceso"** en el ingreso: la cuenta queda pendiente e inactiva y se avisa a los líderes.
  - En Usuarios, la sección **Solicitudes de acceso** (con contador en la pestaña) permite aprobarlas con rol y equipos o rechazarlas, avisando por correo. Tipo de correo nuevo: `Acceso`.
  - **"¿Olvidaste tu contraseña?":** enlace de un solo uso que vence en 60 min. Solo se guarda su hash, máximo 3 por hora, y no pasa por el registro de Notificaciones.
  - **Fuerza bruta:** 5 intentos fallidos para un mismo correo o RUT bloquean el ingreso con contraseña por 15 min. Hay además un límite de envíos por IP.
  - **Auditoría de accesos** (`/usuarios/auditoria`, solo líderes) con filtros Todo / Fallos y bloqueos / Administración.
  - El formulario de usuario agrega **RUT** y, en servidores Windows con AD, **búsqueda en Active Directory**.
- **Asignar a líderes:**
  - Un requerimiento se puede asignar a cualquier **líder activo** (otro líder o uno mismo), además de los analistas activos del equipo del tipo.
  - En los selects de asignación (fila de planificación, detalle, nuevo requerimiento) los líderes aparecen después de los analistas, como "Nombre (líder)". El filtro de analistas también los incluye.
  - Los líderes **no entran** en el balanceo, en el ranking ni en la autoasignación de scripts, ni en el promedio y la dispersión de la planificación: se asignan solo a mano.
  - En la planificación, un líder aparece como fila y tarjeta (etiqueta "Líder") solo si tiene entregables de esa unidad en la semana.
  - La importación reconoce también nombres de líderes en la columna ANALISTA.
  - Un líder con requerimientos abiertos asignados ve la pestaña **Mis asignaciones** (la misma vista que "Mi semana" del analista), con contador de atrasados.
  - Si un líder se asigna a sí mismo, no se envía correo de asignación. Si asigna a otro líder, sí.
- **Usuarios inactivos:** no pueden ingresar, no aparecen en asignaciones, balanceo ni ranking de scripts y se muestran atenuados. Al desactivar a alguien con requerimientos activos se avisa para reasignarlos.
- Un líder no puede desactivarse ni quitarse el rol de líder a sí mismo.
- **Eliminar usuario** (solo líder): botón en la tabla de Usuarios, con confirmación, disponible solo para usuarios **sin historial** (sin entregables terminados, sin OK registrados y sin solicitudes) y nunca para uno mismo. Sus requerimientos abiertos quedan sin asignar. Con historial se desactiva en vez de eliminar.
- El correo debe ser único.
- Usuarios semilla:
  - Líder: Carolina Méndez.
  - DP (desde la planilla): Alexis Duran, José Miguel Carreño, Andrea Pérez, Cristian Gómez, Andrés Salinas, Luis Ruiz y Rodrigo Salgado.
  - Scripting (ficticios): Javiera Pérez, Tomás Herrera y Fernanda Silva.

## 2. Modelo de requerimiento (= entregable)

Una fila de la planilla equivale a un entregable. Un mismo **Job** suele tener varios entregables (BD, Proceso estándar, Tablas, SPSS…).

```ts
type Estado = 'pendiente' | 'en_curso' | 'ok';
interface Requerimiento {
  id: string;
  tipo: 'proc' | 'script' | 'cod';   // cod = Codificación
  fecha: string;        // 'YYYY-MM-DD' — DIA de entrega (proc) o fecha compromiso (script)
  pais: string;         // PAIS (ej. 'CL')
  bu: string;           // BU
  tecnica: string;      // TECNICA
  cliente: string;      // CLIENTE
  proyecto: string;     // PROYECTO (= estudio)
  job: string;          // JOB
  entregable: string;   // ENTREGABLE (script: 'Programación de script')
  descripcion: string;  // DESCRIPCION
  comercial: string;    // COMERCIAL (texto libre, suele venir 'Responsable/Ejecutivo')
  analista: string;     // id Usuario; '' = sin asignar
  estado: Estado;
  creado: string;       // 'YYYY-MM-DD HH:mm'
  okAt: string | null;  // fecha/hora del OK
  okBy: string;         // id del usuario que dio el OK
  okNota: string;
  materialesAt: string; // 'YYYY-MM-DD' fecha en que el analista recibió los materiales ('' = sin registrar)
  horas: number | null; // horas dedicadas, obligatorio al dar OK en proc y script
  registros: number | null; // registros codificados, obligatorio al dar OK en cod
  metodo: '' | 'IA' | 'Manual';
  origen?: 'comercial';  // creado desde una solicitud comercial
  solId?: string;        // 'S-1041' (agrupa los entregables de una misma solicitud)
  solBy?: string;        // id del usuario comercial solicitante
  solAt?: string;        // fecha/hora de la solicitud
  descargadaAt?: string;
  atrasoResp?: '' | 'operaciones' | 'comercial' | 'campo'; // responsable del atraso (obligatorio si el OK es tardío)
  atrasoMotivo?: string;  // motivo dentro del responsable (ver MOTIVOS)
  atrasoDetalle?: string; // texto libre opcional
  atrasoCampo?: '' | 'CATI' | 'F2F' | 'MS' | 'IDP' | 'IVR' | 'IIS' | 'Online' | 'WhatsApp' | 'EFM'; // obligatorio si atrasoResp==='campo' // cuándo el líder la descargó desde Solicitudes nuevas // método de codificación, obligatorio al dar OK en cod
  nota: string;
  actualizado: string;  // última gestión 'YYYY-MM-DD HH:mm'
  alertAt?: string;     // último día en que se envió aviso de atraso
}
```

- **Semana** = lunes de `fecha`; se deriva y no se guarda.
- **Íconos de vencimiento** (solo si `estado !== 'ok'`). Aparecen junto al día en tarjetas, Mi semana, Planificación, Resumen e Histórico, con tooltip y una leyenda en el Tablero:
  - 🟡 **Amarillo**: `fecha` = próximo día hábil (`nextBiz(hoy)`, salta sáb/dom). Un viernes, lo del lunes ya es amarillo. Ícono reloj, relleno `oklch(0.86 0.17 92)`, glifo oscuro.
  - 🔴 **Rojo**: `fecha <= hoy` (vence hoy o vencido). Ícono "!", relleno `oklch(0.57 0.21 27)`, glifo blanco.
  - El rojo usa `--danger`. El amarillo es un tono explícito, porque `--warning` del sitio es ámbar oscuro.
- **Atrasado** = `estado !== 'ok' && fecha < hoy`. Se resalta en accent-900 con negrita.
- Configuración (Tweaks en el mockup, settings en la app):
  - `procCapacity` (10 entregables por analista por semana)
  - `scriptWip` (3 scripts activos simultáneos)
  - `compactCards` (oculta la descripción y la meta en las tarjetas)
  - `codCapacity` (10 entregables por analista de codificación por semana)
  - `staleDays` (2), `notifyAssign` (true) y `notifyOverdue` (true)
- Persistencia del mockup: `localStorage['cargaops.v7'] = { reqs, users, mails }`.
- **Alcance de "semana actual"** (Tablero, Mi tablero y Resumen):
  - Entregables DP con `fecha` en la semana.
  - Scripts abiertos, más los scripts cerrados con `fecha` u `okAt` en la semana.
  - El historial anterior solo aparece en Histórico.

## 3. Reglas de negocio

### 3.1 Procesamiento (planificación semanal)
- Cada **jueves** se arma la semana siguiente. El encabezado muestra "Próximo cierre: Jue dd mmm"; si hoy es jueves, muestra un llamado a publicar la planificación.
- **Carga** = entregables DP + scripts con fecha en la semana asignados al analista (los scripts cuentan para analistas mixtos). La tarjeta muestra el desglose "X DP + Y scripts".
- **Estado de capacidad** (media = entregables de la semana / analistas DP activos):
  - `n > procCapacity` → Sobre capacidad
  - `n − media ≥ 2` → Sobrecargado
  - `media − n ≥ 2` → Subutilizado
  - resto → Equilibrado
- **Sugerir balanceo:** el resultado es una propuesta. El líder la aplica o la descarta.
  1. Los entregables sin analista se agrupan por job y cada grupo va al analista con menor carga.
  2. Se repite mientras `max − min > 1` o alguien supere la capacidad:
     - Se toma un **job completo** del más cargado, con todos sus entregables `pendiente`.
     - El tamaño del job debe ser menor que la diferencia, para que siempre la reduzca. Se prefiere el job más grande y, ante empate, el de fecha más tardía.
     - Ese job pasa al menos cargado.
  3. Nunca se mueven entregables en curso ni con OK. Los jobs no se separan entre analistas. Los scripts suman carga pero el balanceo nunca los mueve.
- **Agenda:** matriz con analistas en filas y Lun–Vie en columnas, más totales. Cada celda muestra cuántos entregables tiene ese analista ese día (si hay scripts: `"3 · 1S"` = 3 DP + 1 script) y se intensifica de accent-100 a accent-300 según la cantidad. Clic en una celda, en un nombre o en una celda de total filtra la tabla; un segundo clic quita el filtro.
- **Tabla de detalle:** mismas columnas que la planilla (Día, Cliente, Proyecto, Job, Entregable, Descripción, Comercial), más Tipo, Analista (select inline filtrado por equipo del tipo), Estado y Okay. Incluye los scripts de la semana de los analistas DP. Tiene búsqueda libre. Orden: sin asignar primero, luego por analista y fecha.
- **Importar planilla:**
  - Formatos: `.xlsx`, `.xls` o `.csv` (con SheetJS) o filas pegadas desde Excel (tab o `;`).
  - Encabezados reconocidos sin importar acentos ni mayúsculas: DIA/FECHA, PAIS, BU, TECNICA, CLIENTE, PROYECTO/ESTUDIO, JOB, ENTREGABLE, DESCRIPCION, COMERCIAL, ANALISTA. Sin encabezado, se asume ese orden.
  - DIA acepta fecha de Excel (Date o serial), `YYYY-MM-DD` o `d/m/yyyy`. Si el segundo número es mayor que 12, se interpreta como `m/d`.
  - La semana se calcula desde DIA.
  - ANALISTA se compara con los analistas DP y los líderes por nombre normalizado (exacto o contenido). Los no reconocidos quedan sin asignar y se listan en la vista previa.
  - **Duplicados** (`fecha|job|entregable|descripcion|analista`, contra la base y dentro del mismo lote) se omiten; la planilla real tiene filas repetidas.
  - La vista previa muestra filas nuevas, semanas detectadas, duplicados, inválidas y analistas no reconocidos.
  - Al importar, todo entra como `pendiente` y se navega a la semana importada.

### 3.0 Solicitudes de áreas comerciales
- **Rol `comercial`:**
  - Tabs: **Nueva solicitud** y **Mis solicitudes**. No ve tablero, planificación ni la carga de nadie.
  - Usuarios semilla: Maria Paz Ayala, Wiliam Muñoz y Lorena Montalvo Rosado.
- **Formulario de nueva solicitud:**
  - Unidad (Procesamiento / Codificación / Script), Cliente*, Job*, Proyecto* y Fecha requerida* (≥ hoy).
  - **SL*** (todas las unidades, incluido Script; se guarda en el campo `bu`): CEX · BHT · MSU · AUM · UU · Observer · CHP.
  - **Técnica*** (combobox, todas las unidades): CATI · F2F · MS · IDP · IVR · IIS · Online · WhatsApp · EFM. Es la misma lista que las áreas de campo (`CAMPOS`), así que `campoFrom()` la reconoce directamente.
  - **Entregables solicitados*** (chips de selección múltiple, una fila por elección):
    - Procesamiento: Proceso de datos · Base de datos cliente Excel · Base de datos SPSS · Tablero · Otros.
    - Codificación: lista `ENT_COD`.
    - Script: se rotula **Tipo de requerimiento*** con Programación · Configurar nueva ola · Cargar base de datos · Cuotas · Mailing.
  - El diálogo **"Nuevo requerimiento" del líder** usa las mismas listas para las tres unidades: SL (`SLS`), Técnica (`CAMPOS`, obligatoria) y Entregable (`ENT_SOL` / `ENT_COD`), o *Tipo de requerimiento* (`ENT_SCRIPT`) en Script. Es selección única, un requerimiento por alta.
  - Descripción y Notas para Operaciones (opcionales).
  - Una línea indica a qué líder llegará la solicitud.
- **Al enviar:**
  - Se crean N requerimientos, uno por entregable, con `analista: ''`, `estado: 'pendiente'`, `origen: 'comercial'`, `solId` correlativo (`S-####`), `solBy`, `solAt` y `comercial = nombre del solicitante`.
  - **No se asignan analistas**, tampoco en Script: la regla de "primer disponible" no aplica aquí.
  - Se envía un correo de tipo `solicitud` a los **líderes del equipo**, con CC al solicitante.
- **Líderes por equipo:**
  - El líder tiene `equipos` = equipos a cargo; vacío significa todos. `teamLeadersOf(users, tipo)` devuelve los líderes de ese equipo o, si no hay, todos los líderes.
  - Todos los líderes siguen viendo todo. El equipo solo define a quién llegan los avisos y el banner.
  - Semilla: Carolina Méndez (Procesamiento), Ricardo Fuentes (Codificación) y Valeria Núñez (Scripting).
- **Mis solicitudes (comercial):** una tarjeta por solicitud.
  - Estado global: Nueva · por asignar / Descargada · por asignar / Asignación parcial / Asignada / En proceso / Entregada.
  - Tabla por entregable: día, asignado a y estado (mismos textos que la consulta pública).
- **Solicitudes nuevas (líder):**
  - Tab con contador rojo de solicitudes sin asignar. En las demás vistas, un banner ámbar muestra las solicitudes de *sus* equipos sin asignar.
  - Filtros: unidad (con conteos) y Por asignar / Todas.
  - **Descargar solicitudes:** mismo `.xlsx` de §3.4 con los entregables filtrados (solo los sin asignar en "Por asignar"). Marca `descargadaAt` y la solicitud pasa a "Descargada".
  - Cada tarjeta tiene el botón **Asignar en Planificación / Scripts →**. En Planificación abre la unidad y semana de la fecha requerida, con búsqueda = Job (los sin asignar aparecen primero). En Scripts usa la cola "sin asignar" existente.
  - La asignación usa los módulos ya creados: select inline, balanceo (que reparte primero los sin asignar) o "Asignar a primer disponible". Cada asignación dispara el correo de asignación habitual.
- **Atrasos:** las solicitudes sin asignar que pasan su fecha entran en el aviso de atraso (van a los líderes).

### 3.1b Codificación (planificación semanal)
- Funciona igual que Procesamiento: planificación semanal armada el jueves, capacidad (`codCapacity`), agenda, balanceo por job completo, importación de planilla (mismas columnas), descarga y correos.
- En **Planificación semanal**, un selector `Procesamiento | Codificación` (`planUnit`) cambia la unidad. Las tarjetas de capacidad, la agenda, la tabla, el balanceo, la importación (`tipo = planUnit`), "Agregar entregable" y la descarga operan sobre esa unidad. Al cambiar de unidad se limpia el filtro de analista.
- Entregables de codificación: Codificación preguntas abiertas, Codificación de marcas, Codificación de ocupación, Libro de códigos, Otros. Por defecto BU = Ops (Operaciones) y Técnica = Codificación.
- Etiqueta de tipo: `COD` (badge `.be-navy`). DP = `.be-neutral`, Script = `.be-info`.
- Un analista puede estar en cualquier combinación de equipos. En el mockup, José Miguel Carreño es DP + Codificación; Marcela Olivares, Pablo Vergara y Daniela Rojas son solo Codificación.

### 3.2 Scripting (ad-hoc)
- **Ranking** de analistas de scripting activos:
  1. Menos scripts activos (pendiente + en curso).
  2. Desempate: menos entregables DP abiertos con fecha ≤ hoy (solo aplica a analistas mixtos).
  3. Desempate: el que tiene el último OK más antiguo, o ninguno.
- El carril de cada analista mixto muestra "N entregables DP hoy".
- El primero del ranking es la recomendación. Si todos están en `scriptWip` o más, se recomienda igual al menos cargado, con aviso.
- Al crear un script con analista "Automático" se asigna de inmediato al recomendado.
- Etiquetas de disponibilidad:
  - `Siguiente`: el primero del ranking
  - `Libre`: 0 activos
  - `Disponible`: menos que el WIP
  - `Al límite`: WIP o más
- Si hay scripts en cola sin asignar, el botón "Asignar a primer disponible" los reparte recalculando el ranking tras cada uno.

### 3.3 OK de término
- **El diálogo de OK pide datos según el tipo:**
  - **Procesamiento y Script:** *Horas dedicadas*, obligatorio, número > 0 y ≤ 200, paso 0,25; acepta coma decimal.
  - **Codificación:** *Registros codificados* (entero > 0) y *Método* `IA | Manual`, ambos obligatorios. No se piden horas.
  - **Todos:** *Fecha de recepción de materiales*, precargada si ya existe y opcional; no puede ser posterior a hoy. Comentario opcional.
  - Si falta un dato obligatorio, se muestra un error en línea y el OK no se guarda.
- **Reabrir o volver a pendiente/en curso** limpia `horas`, `registros` y `metodo`; conserva `materialesAt`.
- **El dato registrado se muestra junto al OK** en tarjetas, tablas, detalle e Histórico ("· 3,5 h" o "· 1.240 reg. · IA").
- **Fecha de materiales:** el analista asignado, o el líder, la ingresa en cualquier momento desde el **detalle** o desde un campo de fecha en cada fila de **Mi semana** (columna "Materiales recibidos").
- Solo el OK cierra un requerimiento y libera disponibilidad.
- Se puede dar arrastrando a "OK · Terminado", con el botón "Dar OK" (tarjeta en curso, tabla o detalle) o desde la lista del analista ("Iniciar" y luego "Dar OK").
- Siempre pide confirmación con comentario opcional. Al confirmar guarda `estado='ok'`, `okAt=ahora`, `okBy=usuario` y `okNota`.
- Solo el líder puede reabrir (vuelve a `en_curso` y limpia los campos de OK).

### 3.3b Filtro por analista (líder)
- Un selector "Todos los analistas / <nombre>" en Tablero, Planificación e Histórico usa un estado compartido (`anFilter`). Al elegir un analista en un tab, el filtro se mantiene en los otros.
- En Planificación, el filtro también se activa con un clic en la tarjeta de capacidad o en el nombre de la agenda: la tarjeta queda resaltada y la tabla muestra solo su semana. Las celdas día × analista siguen funcionando como filtro fino.

### 3.3c Histórico (líder)
- Modo **Semana** o **Mes**, con navegación ‹ › por periodo. Abre por defecto en la semana anterior.
- **KPIs del periodo:**
  - Entregables (DP · scripts)
  - % con OK
  - % OK a tiempo (`okAt` en el día de entrega o antes)
  - Con atraso (OK tardío o abierto vencido)
- **Desglose:**
  - Por día (modo semana) o por semana del mes (modo mes).
  - Barra apilada: ancho = volumen relativo, relleno = con OK.
  - En modo mes, un clic en una semana abre esa semana.
- **Tabla por analista:** DP, Scripts, Con OK, A tiempo y Atrasos. Un clic aplica el filtro de analista.
- **Lista de entregables del periodo:** máximo 100 en pantalla, todos en la descarga, con la columna OK (A tiempo / Tardío / Vencido sin OK).
- KPIs adicionales: **Horas registradas** (suma, y promedio por entregable con horas) y **Registros codificados** (total, desglosado IA · Manual).
- La tabla por analista agrega las columnas COD, Horas y Registros.
- "Descargar periodo" usa el mismo `.xlsx` de §3.4.
- Los datos semilla traen 8 semanas de historia sintética, derivadas de la planilla, con ~10% de OK tardíos.

### 3.3d Notificaciones por correo
- **Asignación** (`notifyAssign`, por defecto activa):
  - Se envía al asignar o reasignar un requerimiento a un analista o a otro líder (no al asignarse uno mismo), sea por el select inline, por el detalle, por un nuevo requerimiento, por el balanceo aplicado, por una importación con analista reconocido o por la autoasignación de scripts.
  - Las acciones masivas envían **un correo por analista** con la lista completa.
  - Para: el analista.
- **Atraso sin gestión** (`notifyOverdue`, por defecto activa):
  - Condición: `estado !== 'ok'`, `fecha < hoy` y **sin gestión**. Sin gestión significa `estado === 'pendiente'` o `actualizado` con `staleDays` días o más de antigüedad (por defecto 2).
  - Para: el analista, con CC a los líderes activos. Si el requerimiento no tiene analista, va solo a los líderes.
  - Un aviso por requerimiento por día (`alertAt = hoy`), agrupado por analista.
  - En producción, un cron diario (por ejemplo 09:00) en lugar de la revisión al cargar. El botón "Enviar recordatorios de atraso ahora" fuerza el envío.
- **Registro de gestión:** cualquier cambio (estado, nota, fecha, analista) actualiza `actualizado`.
- **En la UI:** las tarjetas muestran "Atrasado sin gestión · <día>".
- **Tab Notificaciones:** reglas activas y log de correos (fecha, tipo, para, asunto). Un clic en una fila despliega CC y cuerpo.
- **Formato de los correos:**
  - Asunto `[Carga Ops] Nuevo requerimiento asignado: <job> · <proyecto>`, `[Carga Ops] Tienes N requerimientos nuevos asignados` o `[Carga Ops] N requerimiento(s) atrasado(s) sin gestión`.
  - Cuerpo en texto plano con una línea por entregable: `• día · job · proyecto — entregable: descripción (tipo)`.
- **Producción:** usar un servicio transaccional (SES, SendGrid o Microsoft Graph/Outlook) con una cola. Guardar en una tabla `notificaciones` (id, kind, to, cc, subject, body, req_ids, sent_at, status) y no enviar si el usuario está inactivo.

### 3.3e Motivos de atraso
- **Entrega atrasada** = `estado==='ok' && okAt.slice(0,10) > fecha` (`isLateOk`). Días de atraso = días corridos entre `fecha` y la fecha del OK.
- **Al dar OK con atraso** (hoy > `fecha`), el diálogo de OK muestra un bloque ámbar con:
  - **Responsable** (obligatorio): <unidad del requerimiento> / Área comercial (materiales) / Campo.
  - **Motivo** (obligatorio), según el responsable:
    - Operaciones: Sobrecarga del equipo · Error o retrabajo interno · Complejidad subestimada · Problema técnico / sistemas.
    - Comercial: Materiales entregados tarde · Materiales incompletos o con errores · Cambio de especificaciones.
    - Campo: Cierre de campo tardío · Base de campo con errores · Cuotas no cumplidas.
  - **Área de campo** (obligatoria si el responsable es Campo): CATI · F2F · MS · IDP · IVR · IIS · Online · WhatsApp · EFM.
    - Se precarga desde la Técnica con `campoFrom()`: CATI/telefónica → CATI; intercepción/presencial → F2F; mystery → MS; Qualtrics/CAWI/web → Online; el resto por coincidencia de nombre.
    - Migración: al cargar datos guardados, los atrasos de campo sin `atrasoCampo` se completan con `campoFrom(tecnica)`.
    - Se muestra como "Campo · CATI" en el detalle, la tabla y la descarga (columna AREA CAMPO).
  - **Detalle** (opcional).
  - **Sugerencia automática:** si `materialesAt >= fecha` (materiales recibidos el día de entrega o después), se preselecciona Comercial · "Materiales entregados tarde". Un texto de ayuda explica la evidencia o pide registrar la fecha de materiales.
- **La opción "Operaciones" nunca se muestra con ese nombre.**
  - En el diálogo de OK, el detalle, los KPIs, la barra, los motivos, la tabla de detalle y la descarga aparece como la unidad del requerimiento: **Procesamiento**, **Script** o **Codificación**.
  - El valor guardado sigue siendo `atrasoResp: 'operaciones'`; la etiqueta se resuelve con `effResp(r)` = `'op_'+r.tipo`.
  - Colores: Procesamiento `--teal-text`, Script `--teal`, Codificación teal claro.
  - Los KPIs muestran "Causa Procesamiento / Script / Codificación" (solo la unidad filtrada, si hay filtro). En la tabla por unidad, la columna se llama "Propio de la unidad".
- **Corrección posterior:** en el detalle de una entrega atrasada, el líder o el analista asignado pueden corregir responsable y motivo. Reabrir o mover desde OK limpia los campos de atraso.
- **Tab "Motivos de atraso" (líder):**
  - Filtros: mismo periodo y navegación que Histórico (estado compartido), unidad (Todas / Procesamiento / Scripting / Codificación) y analista.
  - **KPIs:** entregas con atraso (% sobre las entregas con OK), días promedio, % de causa Operaciones, % de causa materiales comerciales, % de causa campo y vencidos sin cerrar.
  - **Barra apilada por responsable:** Operaciones = `--teal`, Comercial = `--warning`, Campo = `--ink-muted`, Sin clasificar = `--border`. Con conteo, % y días promedio.
  - **Tabla por unidad:** entregas, atrasos (%), desglose Operaciones / Comercial / Campo y días promedio.
  - **Motivos más frecuentes:** top 8, con barra.
  - **Atrasos por materiales, por responsable comercial:** se agrupa por la primera parte de `comercial`, antes de "/", y se muestra el motivo principal.
  - **Atrasos de campo, por área** (`atrasoCampo`): CATI, F2F, MS, IDP, IVR, IIS, Online, WhatsApp y EFM, con días promedio y motivo principal.
  - **Detalle:** comprometido, entregado, +días, tipo, job, proyecto, analista, materiales recibidos ("· tarde" en ámbar si `>= fecha`), responsable y motivo. Ordenado por días de atraso. Un clic abre el detalle.
  - "Descargar atrasos" usa el `.xlsx` de §3.4.
- **Datos semilla:** ~20% de las entregas históricas con OK tardío, clasificadas de forma determinística (≈40% Operaciones, 30% Comercial, 30% Campo; los scripts solo Operaciones o Comercial).
- **Producción:** guardar el motivo en `requerimientos` y en `ok_log`. Endpoint `GET /kpi/atrasos?desde=&hasta=&unidad=&analista=`.

### 3.4 Descargar planificación
- Botón **Descargar planificación** (líder, en Planificación) y **Descargar mi semana** (analista, en Mi semana).
- Genera un `.xlsx` (SheetJS) con:
  - Hoja `agenda`, con las columnas de la planilla original (DIA…ANALISTA) más TIPO, ESTADO, MATERIALES RECIBIDOS, FECHA OK, HORAS, REGISTROS CODIFICADOS, METODO, SOLICITUD, SOLICITADO POR, FECHA SOLICITUD, DIAS ATRASO, RESPONSABLE ATRASO, MOTIVO ATRASO y DETALLE ATRASO, ordenada por día, analista y job.
  - Hoja `resumen`, por analista: entregables DP, scripts, codificación, abiertos, con OK, horas y registros codificados.
- El líder descarga todos los entregables DP de la semana más todos los scripts con fecha en esa semana. El analista descarga solo lo suyo, incluidos los atrasados abiertos.
- Nombre del archivo: `Planificacion_<lunes ISO>.xlsx` / `Mi_semana_<Nombre>_<lunes ISO>.xlsx`. Si la librería no carga, descarga CSV (`;`, UTF-8 BOM).

### 3.5 Consulta pública (solo lectura)
- Acceso: link en el login, URL `#consulta`, o deep link `#consulta?job=<JOB>`. En el detalle, "Copiar enlace de consulta" genera ese link para enviarlo al comercial.
- Búsqueda (mínimo 3 caracteres) por Job, proyecto o cliente. Los resultados se agrupan por Job (máximo 20).
- Cada job muestra Job · Cliente, Proyecto, Comercial, un estado global y la próxima entrega.
  - Estados globales: Entregado / Por asignar / Entregado parcial / En proceso / Asignado.
- Tabla de entregables: Entregable — Descripción, Tipo, Día de entrega (+ "fuera de plazo"), Asignado a y Estado.
  - Estados: Por asignar / Asignado · programado / En proceso / Entregado <fecha>.
- No muestra notas, comentarios de OK ni acciones. En producción, exponer un endpoint de solo lectura (`GET /consulta?q=`) protegido por SSO corporativo, sin requerir rol en la app.

## 4. Pantallas

| Pantalla | Rol | Contenido |
| --- | --- | --- |
| **Login** | — | Marca, correo y contraseña, error si el correo no existe o el usuario está inactivo, y accesos de demostración agrupados por rol. |
| **Header** | todos | Marca, tabs según rol, "Nuevo requerimiento" (solo líder), chip con iniciales, nombre y rol, y "Salir". |
| **Banda "Mi carga"** | analista | Pendientes, en curso, atrasados y con OK de la semana. Botón "Semana siguiente: N entregables →" para revisar lo planificado el jueves. |
| **Tablero** | todos | Kanban Pendiente / En curso / OK con drag & drop. Alcance: procesamiento de la semana actual más scripts; el analista solo ve lo propio. Búsqueda; filtro de equipo solo para el líder. Tarjeta: `job · entregable`, día (o "Atrasado · día"), proyecto, descripción, `cliente · comercial`, analista y "Dar OK" o la fecha del OK. Orden por fecha. |
| **Mi semana** | analista | DP y scripts juntos (columna Tipo), semana actual o siguiente (la actual incluye atrasados abiertos). Acción por fila: Iniciar → Dar OK → ✓ fecha. Las filas con OK van al final. Botón "Descargar mi semana". |
| **Consulta pública** | sin cuenta | Buscador y tarjetas por Job (ver §3.5). |
| **Planificación semanal** | líder | Selector de semanas (anterior, actual, siguiente y futuras con datos), tarjetas de capacidad, indicadores, propuesta de balanceo, agenda, tabla de detalle, importar planilla y agregar entregable. |
| **Scripts** | líder | Recomendación de la siguiente asignación con "Ingresar script", un carril por analista en orden de ranking y cola de scripts sin asignar. |
| **Resumen** | líder | KPIs, pendientes priorizados (por fecha, atrasados primero), carga por analista con atrasados, texto listo para enviar y botón para copiarlo. |
| **Solicitudes nuevas** | líder | Ver §3.0. |
| **Nueva solicitud / Mis solicitudes** | comercial | Ver §3.0. |
| **Motivos de atraso** | líder | Ver §3.3e. |
| **Histórico** | líder | Ver §3.3c. |
| **Notificaciones** | líder | Ver §3.3d. |
| **Usuarios** | líder | Tarjetas por rol con su descripción de permisos y tabla de usuarios: nombre, correo, rol, equipo, carga activa, botón Activo/Inactivo y editar. Diálogo de nuevo o editar: nombre, correo, rol y equipo (si es analista). |
| **Detalle** | todos | Datos (día, cliente, comercial, BU, técnica, analista). Solo el líder edita analista y fecha. Estado, notas, info del OK (quién y cuándo). Eliminar y Reabrir solo para el líder. |
| **Nuevo requerimiento** | líder | Tipo; Cliente*, Job* y Proyecto*. Para proc: BU, Técnica y Entregable. Fecha, Descripción, Comercial y Analista (en scripts, "Automático" con la recomendación en vivo). |
| **Importar** | líder | Zona de archivo, textarea para pegar, vista previa y botón Importar. |

## 5. Formato del resumen

```
Resumen de carga — Mar 20 oct

PROCESAMIENTO · semana 19 – 23 oct
• Entregables con OK: 12 de 49
• Pendientes de OK (37):
  - Mar 20 oct · 25-098794-09-07 · 1. CLA Global Muestra Aleatoria Mensual — Tablas Estadísticas — Andrea Pérez (En curso)
  ...

SCRIPTING
• Activos: 3 · Con OK: 4
• Pendientes de OK (3):
  - ...

Recordatorio: confirmar con "Dar OK" en Carga Ops al terminar cada entregable.
```

## 6. Implementación — stack objetivo (.NET 10 + Blazor Server)

> El mockup es HTML/JS de referencia. **No incrustar ese HTML**: hay que traducir cada pantalla a componentes Razor y cada función de la clase `Component` a servicios C# probados. La lógica del mockup es la especificación funcional.

### 6.1 Stack
| Capa | Tecnología | Versión |
| --- | --- | --- |
| Runtime / lenguaje | .NET LTS / C# | 10.0.x / 14 |
| UI | Blazor Web App, render **Interactive Server** | ASP.NET Core 10 |
| Tiempo real | SignalR (circuito Blazor) + hub propio para difusión | ASP.NET Core 10 |
| Estilos | Bootstrap 5.3 (plantilla) + `wwwroot/css/site.css` (tokens de `uploads/site[1].css`) | 5.3.x |
| Kanban | Eventos HTML5 nativos (`@ondragstart`, `@ondragover:preventDefault`, `@ondrop`) | — |
| Lógica | Servicios C# en capa de aplicación | 10.0.x |
| Datos | EF Core + `Microsoft.EntityFrameworkCore.SqlServer`, migraciones en el repo | 10.0.x |
| BD | SQL Server 2025 (Developer en desarrollo; Express o Standard en producción, no Web) | 2025 CU |
| Auth | Windows Authentication (Negotiate) con AD, o ASP.NET Core Identity si no hay dominio | 10.0.x |
| Excel | **ClosedXML** (MIT), para importar y exportar `.xlsx` con el formato de la planilla | última estable |
| Correo | `MailKit` (SMTP) o Microsoft Graph `sendMail` (Exchange Online) detrás de `IEmailSender` | última estable |
| Pruebas | xUnit (reglas) + bUnit (componentes) + Testcontainers.MsSql (integración, opcional) | última estable |
| IDE / BD | Visual Studio 2026 (18.x), SSMS 22 | — |
| VCS | Git + GitHub (punto de encuentro entre Claude Code Web y Visual Studio) | — |

### 6.2 Solución
```
CargaOps.slnx
├─ src/
│  ├─ CargaOps.Domain/          Entidades, enums, constantes de catálogos (sin dependencias)
│  ├─ CargaOps.Application/     Servicios de reglas (asignación, balanceo, importación, KPIs, notificaciones), DTOs, interfaces
│  ├─ CargaOps.Infrastructure/  EF Core DbContext, migraciones, repositorios, ClosedXML, IEmailSender, hosted services
│  └─ CargaOps.Web/             Blazor Web App (Interactive Server), componentes Razor, hub SignalR, auth
└─ tests/
   ├─ CargaOps.Application.Tests/  xUnit
   └─ CargaOps.Web.Tests/          bUnit
```

### 6.3 Modelo de datos (EF Core)
Los nombres en el mockup → C#:
- **`Usuario`**: Id, Nombre, Email (único), Rol (`Lider|Analista|Comercial`), Activo, `DomainAccount` (para Windows Auth, `DOMINIO\\usuario`).
- **`UsuarioEquipo`** (N:M): UsuarioId, Equipo (`Procesamiento|Script|Codificacion`). Para un líder, son los equipos a cargo; sin filas = todos.
- **`Requerimiento`**:
  - Id, Tipo (Equipo), Fecha (`DateOnly`), Pais, SL (`bu` en el mockup), Tecnica, Cliente, Proyecto, Job, Entregable, Descripcion, Comercial, AnalistaId?, Estado (`Pendiente|EnCurso|Ok`).
  - Seguimiento: Creado, Actualizado, MaterialesAt (`DateOnly?`), Nota.
  - Cierre: OkAt?, OkById?, OkNota, Horas (`decimal(6,2)?`), Registros (`int?`), Metodo (`IA|Manual`?).
  - Atraso: AtrasoResp (`Operaciones|Comercial|Campo`?), AtrasoMotivo, AtrasoCampo, AtrasoDetalle.
  - Solicitud y alertas: SolicitudId?, AlertAt (`DateOnly?`).
  - Columna `rowversion` para concurrencia.
  - Índices: `(Tipo, Fecha)`, `AnalistaId`, `Job`, `SolicitudId`.
- **`Solicitud`**: Id, Codigo (`S-####`, secuencia SQL), SolicitanteId, CreadaAt, DescargadaAt?, Nota. Relación 1:N con `Requerimiento`.
- **`Notificacion`**: Id, Kind (`Asignacion|Atraso|Solicitud`), To, Cc, Subject, Body, ReqIds (JSON), CreatedAt, SentAt?, Status, Error.
- **`OkLog`** (auditoría): ReqId, Accion (`Ok|Reabrir|CambioEstado|Asignacion`), UsuarioId, At, Payload (JSON).
- **Catálogos como `static readonly` en Domain** (o tablas si deben editarse sin despliegue): `SLS`, `CAMPOS`, `ENT_SOL`, `ENT_SCRIPT`, `ENT_COD`, `MOTIVOS` y las BU históricas de la planilla.
- **Configuración** (`appsettings` → `IOptions<ReglasOptions>`): ProcCapacity=10, CodCapacity=10, ScriptWip=3, StaleDays=2, NotifyAssign, NotifyOverdue.

### 6.4 Servicios (Application) — portar desde el mockup
| Servicio | Funciones del mockup | Notas |
| --- | --- | --- |
| `IScriptAssignmentService` | `rank`, `autoAssignScripts` | Ranking: menos activos → menos entregables DP abiertos hoy → último OK más antiguo. |
| `IBalanceService` | `suggestBalance`, `applyBalance` | Mueve jobs completos solo si todo el job está pendiente. Devuelve la propuesta y aplica en una transacción. |
| `IImportService` | `parseMatrix`, `toISODate`, `doImport` | ClosedXML, normalización de encabezados, duplicados por `fecha|job|entregable|descripcion|analista`. |
| `IExportService` | `download` | Hojas `agenda` + `resumen`; columnas de §3.4. |
| `IOkService` | `confirmOk`, `reopen`, `moveTo` | Valida horas, o registros + método, materiales ≤ hoy y motivo de atraso. Registra en `OkLog`. |
| `ISolicitudService` | `submitSol`, `downloadInbox` | Crea la solicitud y sus entregables sin analista, y notifica a `teamLeadersOf`. |
| `IKpiService` | histórico, motivos de atraso | Consultas agregadas con EF y `GroupBy`, o vistas SQL. |
| `INotificationService` | `buildMail`, `notifyAssign`, `checkOverdue`, `solMailObj` | Encola en `Notificacion`; `EmailDispatcher` (BackgroundService) envía y reintenta. |
| `ISummaryService` | `buildSummary` | Texto plano para "Copiar resumen". |

- **Hosted services:**
  - `OverdueCheckJob`: diario, 09:00 hora de Chile (`America/Santiago`), con `PeriodicTimer`. Equivale a `checkOverdue(false)`.
  - `ThursdayReminderJob` (opcional).
  - `EmailDispatcher`.
- **Fechas:** `DateOnly` para fechas de entrega y materiales, `DateTimeOffset` para timestamps. Hoy y el próximo día hábil se calculan con `TimeProvider`, inyectable en los tests.

### 6.5 UI — mapeo de pantallas a componentes Razor
| Mockup | Ruta / componente | Política |
| --- | --- | --- |
| Login | Windows Auth: sin pantalla. Identity: `/Account/Login` | — |
| Consulta pública | `/consulta`, `Pages/Consulta.razor` (`?job=`) | `[Authorize]` sin rol (solo autenticado) |
| Layout (topbar navy + menú lateral) | `Layout/MainLayout.razor`, `NavMenu.razor` (ítems según rol) | — |
| Tablero | `/`, `Pages/Tablero.razor` + `KanbanColumn.razor` + `ReqCard.razor` | Lider, Analista |
| Mi semana | `/mi-semana` | Analista |
| Solicitudes nuevas | `/solicitudes` | Lider |
| Nueva solicitud / Mis solicitudes | `/solicitar`, `/mis-solicitudes` | Comercial |
| Planificación semanal | `/planificacion?unidad=&semana=` + `CapacityCard`, `AgendaGrid`, `BalanceProposal` | Lider |
| Scripts | `/scripts` + `ScriptLane.razor` | Lider |
| Resumen | `/resumen` | Lider |
| Histórico | `/historico` | Lider |
| Motivos de atraso | `/atrasos` (barras con divs y CSS, sin librería de gráficos) | Lider |
| Notificaciones | `/notificaciones` | Lider |
| Usuarios | `/usuarios` | Lider |
| Diálogos | `ReqDetailModal`, `OkModal`, `NewReqModal`, `ImportModal`, `UserModal`: modales Bootstrap controlados por estado C#, sin `bootstrap.js` | — |

- **Autorización:**
  - Políticas `Lider`, `Analista` y `Comercial`.
  - En los servicios, filtrar siempre por usuario: el analista solo ve `AnalistaId == me` y el comercial solo `SolicitanteId == me`. **No confiar en la UI.**
  - Con Windows Auth, mapear `User.Identity.Name` a `Usuario.DomainAccount` con un `IClaimsTransformation` que agregue el claim de rol.
- **Tiempo real:**
  - `CargaHub` (SignalR) emite `ReqChanged(reqId)`, `SolicitudCreada(id)` y `NotificacionNueva`.
  - Los componentes se suscriben con `HubConnection` o con un servicio singleton `ChangeNotifier` con eventos C#. Esta segunda opción es más simple en Interactive Server, porque todos los circuitos corren en el mismo proceso.
  - En ambos casos, llamar a `InvokeAsync(StateHasChanged)`.
- **Drag & drop:** `draggable="true"`, `@ondragstart` guarda el id en el estado del componente, `@ondragover:preventDefault` y `@ondrop` llama a `IOkService.MoveTo`. Soltar en OK abre `OkModal`.
- **Descargas:** endpoint minimal API `GET /export/...` que devuelve `File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name)`. Desde Blazor se navega a la URL. **Subida:** `<InputFile accept=".xlsx,.csv">`.
- **Estilos:**
  - Copiar `uploads/site[1].css` a `wwwroot/css/site.css` junto a Bootstrap 5.3.
  - Clases del mockup → Bootstrap:
    - `.btn-primary` (navy) → `.btn-primary`
    - `.btn-secondary` → `.btn-outline-secondary`
    - `.input` → `.form-control` / `.form-select`
    - `.seg` → `.btn-group` + `.btn-check`
    - `.dialog` → `.modal`
    - Badges de estado `.be-*`, topbar, menú lateral (`.side`, `.nav-i`) y `.cnt`: tal como están en `carga-ops.css`.
  - Íconos: Lucide como SVG inline, en un componente `Icon.razor`.

### 6.6 Entorno y conexión
- La cadena de conexión **nunca va en `appsettings.json`**. Usar `appsettings.Development.json` (gitignored) o `dotnet user-secrets`, con la clave `ConnectionStrings:CargaOps`.
- **Claude Code Web (Linux):** sin LocalDB. Usar SQL Server 2025 en contenedor (`mcr.microsoft.com/mssql/server:2025-latest`) o Testcontainers en los tests de integración.
- **Visual Studio (Windows):** instancia local de SQL Server 2025 Developer. Se clona desde GitHub y se abre el `.slnx` directamente.
- **Migraciones:**
  - `dotnet ef migrations add <Nombre> -p src/CargaOps.Infrastructure -s src/CargaOps.Web`. Se versionan en el repo.
  - Agregar un seeder idempotente con los usuarios y datos de la planilla (`uploads/Prog DP.xlsx`) solo en Development.
- **Producción:** SQL Server 2025 Express o Standard (la edición Web se descontinúa). IIS o Kestrel en Windows Server con Windows Auth.

### 6.7 Pruebas mínimas
- **xUnit:**
  - Ranking de scripts (empates, WIP lleno, analistas mixtos).
  - Balanceo (no separa jobs, no mueve en curso ni OK, converge a una diferencia ≤ 1).
  - `ToIsoDate` (serial de Excel, `d/m/yyyy`, `m/d` cuando el día es mayor que 12).
  - Duplicados en la importación.
  - Validaciones del OK (horas, registros + método, materiales futuros, motivo y área de campo si hay atraso).
  - `campoFrom`, `effResp`, sin gestión y atrasado.
  - Próximo día hábil (viernes → lunes).
  - `teamLeadersOf`.
- **bUnit:**
  - `ReqCard`: íconos amarillo y rojo según la fecha, `TimeProvider` simulado.
  - `OkModal`: campos según el tipo y bloque de atraso.
  - `NavMenu`: ítems por rol.
  - `Consulta`: no muestra notas.
- **Integración:** filtros de seguridad por rol en los servicios.

### 6.8 Flujo Claude Design → Claude Code Web → GitHub → Visual Studio
1. Este README y `Carga Ops.dc.html` (abrible en el navegador) son el handoff. `carga-ops.css` es la referencia de estilos.
2. Claude Code Web crea la solución (§6.2), las entidades y migraciones (§6.3), los servicios con tests (§6.4) y luego la UI (§6.5), y hace push a GitHub.
3. En Visual Studio 2026: Git → Clonar repositorio → abrir `.slnx`. Configurar user-secrets con la instancia local → `Update-Database` → F5.
4. Cada cambio de diseño posterior se hace primero en el mockup y en este README (regla de `CLAUDE.md`); Claude Code sincroniza a partir del diff del README.

### 6.9 Endpoints / operaciones (referencia funcional)
En Blazor Server la mayoría son llamadas directas a servicios, no HTTP. Solo se exponen como minimal API las descargas, la subida de archivos y, si se requiere, la consulta pública.
- `GET /me`
- `GET /requerimientos?semana=&tipo=` (un analista solo recibe los propios)
- `POST|PATCH|DELETE /requerimientos` (solo líder; el analista solo puede `PATCH estado`)
- `POST /requerimientos/:id/ok` (asignado o líder)
- `POST /requerimientos/:id/reabrir` (líder)
- `POST /planificacion/:semana/balanceo` (devuelve la propuesta) y `POST /planificacion/:semana/aplicar`
- `POST /planificacion/importar` (multipart xlsx)
- `GET /planificacion/:semana/export.xlsx` (líder: todo; analista: lo propio)
- `POST /solicitudes` (comercial) y `GET /solicitudes?estado=&unidad=`
- `GET /solicitudes/export.xlsx` (marca `DescargadaAt`)
- `GET /consulta?q=` (solo lectura, autenticado)
- `GET /historico?desde=&hasta=&analista=` y `GET /kpi/atrasos?desde=&hasta=&unidad=&analista=`
- `GET /notificaciones` (líder)
- `GET|POST|PATCH /usuarios` (líder)
- `GET /scripts/recomendacion`
