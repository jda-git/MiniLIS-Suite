// Reloj de inactividad del puesto. Lo lleva el navegador porque es donde se sabe si alguien
// está usando el equipo; el componente SessionTimeout.razor solo recibe el aviso.
//
// No sustituye a la caducidad de la cookie: es lo que hace que también caduque una pestaña
// abierta, que en Blazor Server no genera peticiones HTTP y por tanto nunca caducaría sola.
window.minilisInactividad = (() => {
    let ref = null;
    let limite = 0;          // segundos sin actividad tras los que se cierra la sesión
    let antelacion = 0;      // segundos de aviso previo
    let ultima = Date.now();
    let temporizador = null;
    let avisando = false;
    let cerrando = false;

    const eventos = ['mousedown', 'mousemove', 'keydown', 'wheel', 'touchstart', 'scroll', 'click'];

    const actividad = () => {
        ultima = Date.now();
        if (avisando && ref) {
            avisando = false;
            ref.invokeMethodAsync('Cancelar');
        }
    };

    const latido = () => {
        if (cerrando || !ref) return;
        const inactivo = Math.floor((Date.now() - ultima) / 1000);

        if (inactivo >= limite) {
            cerrando = true;
            ref.invokeMethodAsync('Cerrar');
            return;
        }
        if (inactivo >= limite - antelacion) {
            avisando = true;
            // Cada segundo durante la cuenta atrás: es lo que mueve el número del aviso.
            ref.invokeMethodAsync('Avisar', limite - inactivo);
        }
    };

    return {
        iniciar: (dotNetRef, segundosLimite, segundosAviso) => {
            window.minilisInactividad.parar();
            ref = dotNetRef;
            limite = segundosLimite;
            antelacion = segundosAviso;
            ultima = Date.now();
            avisando = false;
            cerrando = false;
            eventos.forEach(e => document.addEventListener(e, actividad, { passive: true }));
            // Cada segundo: fuera de la cuenta atrás no hace nada salvo una resta.
            temporizador = setInterval(latido, 1000);
        },

        reiniciar: () => actividad(),

        parar: () => {
            if (temporizador) { clearInterval(temporizador); temporizador = null; }
            eventos.forEach(e => document.removeEventListener(e, actividad));
            ref = null;
            avisando = false;
        }
    };
})();
