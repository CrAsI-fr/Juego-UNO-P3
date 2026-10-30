drop database if exists unobd;
create database unobd;
use unobd;

-- ============================================================
-- CARTA: catalogo del mazo (60 cartas por ahora).
-- Cada fila es una carta fisica; las repetidas tienen id distinto.
-- ============================================================
create table `unobd`.`carta` (
	`id_carta` int not null auto_increment,
    `tipo_carta` enum('ordinaria', 'especial') not null,
    `color_carta` enum('rojo', 'amarillo', 'azul', 'verde') null,
    `efecto` enum('cambiar_color', 'cambiar_direccion', 'bloquear', '+2', '+4') null,
    `num_carta` int null,
    primary key (`id_carta`),
    constraint chk_num_carta check (num_carta <= 9 and num_carta >= 0),
    -- ordinaria: color + numero, sin efecto
    -- especial comodin (cambiar_color, +4): sin color ni numero
    -- especial de color (bloquear, +2, cambiar_direccion): color, sin numero
    constraint chk_carta_consistente check (
        (tipo_carta = 'ordinaria'
            and color_carta is not null and num_carta is not null and efecto is null)
        or (tipo_carta = 'especial' and efecto in ('cambiar_color', '+4')
            and color_carta is null and num_carta is null)
        or (tipo_carta = 'especial' and efecto in ('bloquear', '+2', 'cambiar_direccion')
            and color_carta is not null and num_carta is null)));

-- ============================================================
-- JUGADOR: solo datos permanentes. Las cartas en mano y el estado
-- del turno viven en memoria del programa en C#, no aqui.
-- ============================================================
create table `unobd`.`jugador` (
	`id_jugador` int not null auto_increment,
    `nombre_jug` varchar(30) not null,
    primary key (`id_jugador`),
    unique key `uq_nombre_jug` (`nombre_jug`));

-- ============================================================
-- PARTIDA: id_ganador queda null mientras la partida esta en curso
-- (o si se abandona).
-- ============================================================
create table `unobd`.`partida` (
	`id_partida` int not null auto_increment,
    `fecha_inicio` timestamp not null default current_timestamp,
    `fecha_fin` timestamp null default null,
    `id_ganador` int null default null,
    primary key (`id_partida`),
    key `id_ganador` (`id_ganador`),
    constraint `fk_partida_ganador`
		foreign key (`id_ganador`)
        references `jugador` (`id_jugador`));

-- ============================================================
-- PARTIDA_JUGADOR: quien participo en cada partida y en que orden
-- se sienta. Sin esta tabla no se pueden contar las derrotas.
-- ============================================================
create table `unobd`.`partida_jugador` (
	`id_partida` int not null,
    `id_jugador` int not null,
    `posicion` int not null,
    primary key (`id_partida`, `id_jugador`),
    unique key `uq_partida_posicion` (`id_partida`, `posicion`),
    key `id_jugador` (`id_jugador`),
    constraint `fk_pj_partida`
		foreign key (`id_partida`)
        references `partida` (`id_partida`),
	constraint `fk_pj_jugador`
		foreign key (`id_jugador`)
        references `jugador` (`id_jugador`));

-- ============================================================
-- JUGADA: log de TODOS los movimientos de la partida.
--   carta_inicial    -> carta volteada al inicio (sin jugador)
--   repartir         -> carta que recibe el jugador en el reparto inicial
--   tirar            -> el jugador tira id_carta
--   robar            -> el jugador roba id_carta del mazo
--   pasar            -> el jugador pasa turno (sin carta)
--   decir_uno        -> el jugador canta UNO (sin carta)
--   penalizacion_uno -> no dijo UNO; roba id_carta como castigo
-- color_elegido solo aplica al tirar un cambiar_color o +4.
-- ============================================================
create table `unobd`.`jugada` (
	`id_jugada` int not null auto_increment,
    `id_partida` int not null,
    `num_jugada` int not null,
    `id_jugador` int null default null,
    `tipo_accion` enum('carta_inicial', 'repartir', 'tirar', 'robar', 'pasar', 'decir_uno', 'penalizacion_uno') not null,
    `id_carta` int null default null,
    `color_elegido` enum('rojo', 'amarillo', 'azul', 'verde') null default null,
    `fecha_hora` timestamp not null default current_timestamp,
    primary key (`id_jugada`),
    unique key `uq_partida_num_jugada` (`id_partida`, `num_jugada`),
    key `id_jugador` (`id_jugador`),
    key `id_carta` (`id_carta`),
    constraint `fk_jugada_partida`
		foreign key (`id_partida`)
        references `partida` (`id_partida`),
	constraint `fk_jugada_pj`
		foreign key (`id_partida`, `id_jugador`)
        references `partida_jugador` (`id_partida`, `id_jugador`),
	constraint `fk_jugada_carta`
		foreign key (`id_carta`)
        references `carta` (`id_carta`),
	constraint chk_jugada_jugador check (
        (tipo_accion = 'carta_inicial' and id_jugador is null)
        or (tipo_accion <> 'carta_inicial' and id_jugador is not null)),
	constraint chk_jugada_carta check (
        (tipo_accion in ('carta_inicial', 'repartir', 'tirar', 'robar', 'penalizacion_uno') and id_carta is not null)
        or (tipo_accion in ('pasar', 'decir_uno') and id_carta is null)),
	constraint chk_jugada_color check (
        color_elegido is null or tipo_accion in ('tirar', 'carta_inicial')));

-- ============================================================
-- VISTAS
-- ============================================================

-- Historial de ganadas / perdidas por jugador (solo partidas terminadas)
create view `unobd`.`vista_historial` as
select j.id_jugador,
       j.nombre_jug,
       count(p.id_partida) as partidas_jugadas,
       coalesce(sum(p.id_ganador = j.id_jugador), 0) as ganadas,
       coalesce(sum(p.id_ganador <> j.id_jugador), 0) as perdidas
from jugador j
left join partida_jugador pj on pj.id_jugador = j.id_jugador
left join partida p on p.id_partida = pj.id_partida and p.id_ganador is not null
group by j.id_jugador, j.nombre_jug;

-- Log legible de movimientos
create view `unobd`.`vista_log` as
select ju.id_partida,
       ju.num_jugada,
       ju.fecha_hora,
       coalesce(jg.nombre_jug, '(mesa)') as jugador,
       ju.tipo_accion,
       c.tipo_carta,
       c.color_carta,
       c.num_carta,
       c.efecto,
       ju.color_elegido
from jugada ju
left join jugador jg on jg.id_jugador = ju.id_jugador
left join carta c on c.id_carta = ju.id_carta;

-- ============================================================
-- DATOS
-- ============================================================

-- Los jugadores se dan de alta desde el juego (boton "Empezar juego")

insert into carta(tipo_carta, color_carta, efecto, num_carta) values
	('ordinaria', 'rojo', null, 1),
    ('ordinaria', 'rojo', null, 2),
    ('ordinaria', 'rojo', null, 3),
    ('ordinaria', 'rojo', null, 4),
    ('ordinaria', 'rojo', null, 5),
    ('ordinaria', 'rojo', null, 6),
    ('ordinaria', 'rojo', null, 7),
    ('ordinaria', 'rojo', null, 8),
    ('ordinaria', 'rojo', null, 9),
    ('ordinaria', 'amarillo', null, 1),
    ('ordinaria', 'amarillo', null, 2),
    ('ordinaria', 'amarillo', null, 3),
    ('ordinaria', 'amarillo', null, 4),
    ('ordinaria', 'amarillo', null, 5),
    ('ordinaria', 'amarillo', null, 6),
    ('ordinaria', 'amarillo', null, 7),
    ('ordinaria', 'amarillo', null, 8),
    ('ordinaria', 'amarillo', null, 9),
    ('ordinaria', 'azul', null, 1),
    ('ordinaria', 'azul', null, 2),
    ('ordinaria', 'azul', null, 3),
    ('ordinaria', 'azul', null, 4),
    ('ordinaria', 'azul', null, 5),
    ('ordinaria', 'azul', null, 6),
    ('ordinaria', 'azul', null, 7),
    ('ordinaria', 'azul', null, 8),
    ('ordinaria', 'azul', null, 9),
    ('ordinaria', 'verde', null, 1),
    ('ordinaria', 'verde', null, 2),
    ('ordinaria', 'verde', null, 3),
    ('ordinaria', 'verde', null, 4),
    ('ordinaria', 'verde', null, 5),
    ('ordinaria', 'verde', null, 6),
    ('ordinaria', 'verde', null, 7),
    ('ordinaria', 'verde', null, 8),
    ('ordinaria', 'verde', null, 9);

insert into carta(tipo_carta, color_carta, efecto, num_carta) values
	('especial', null, 'cambiar_color', null),
    ('especial', null, 'cambiar_color', null),
    ('especial', null, '+4', null),
    ('especial', null, '+4', null),
    ('especial', 'rojo', 'bloquear', null),
    ('especial', 'rojo', 'bloquear', null),
    ('especial', 'amarillo', 'bloquear', null),
    ('especial', 'amarillo', 'bloquear', null),
    ('especial', 'azul', 'bloquear', null),
    ('especial', 'azul', 'bloquear', null),
    ('especial', 'verde', 'bloquear', null),
    ('especial', 'verde', 'bloquear', null),
    ('especial', 'rojo', '+2', null),
    ('especial', 'rojo', '+2', null),
    ('especial', 'amarillo', '+2', null),
    ('especial', 'amarillo', '+2', null),
    ('especial', 'azul', '+2', null),
    ('especial', 'azul', '+2', null),
    ('especial', 'verde', '+2', null),
    ('especial', 'verde', '+2', null),
    ('especial', 'rojo', 'cambiar_direccion', null),
    ('especial', 'amarillo', 'cambiar_direccion', null),
    ('especial', 'azul', 'cambiar_direccion', null),
    ('especial', 'verde', 'cambiar_direccion', null);
