// Este app nao usa Service Worker. Este arquivo existe para desinstalar um Service Worker de outro projeto
// (ex.: vite-plugin-pwa, que registra /sw.js no build e /dev-sw.js no modo dev) que tenha rodado antes em localhost:5173:
// ele continuaria servindo a pagina do outro projeto e o app ficaria em branco. Na proxima visita o navegador
// busca este script, instala no lugar do antigo, limpa os caches, cancela o registro e recarrega as abas.
self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      const keys = await caches.keys();
      await Promise.all(keys.map((key) => caches.delete(key)));
      await self.registration.unregister();
      const windows = await self.clients.matchAll({ type: 'window' });
      await Promise.all(windows.map((client) => client.navigate(client.url)));
    })(),
  );
});
