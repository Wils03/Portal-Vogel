// Funções chamadas pelo Blazor via JS interop.
window.portal = {
    baixarArquivo: (nome, base64, tipo) => {
        const link = document.createElement('a');
        link.href = `data:${tipo};base64,${base64}`;
        link.download = nome;
        document.body.appendChild(link);
        link.click();
        link.remove();
    },

    graficos: {},

    // series: [{ label, data, tipo ('bar'|'line'), cor }]
    grafico: (id, rotulos, series) => {
        const canvas = document.getElementById(id);
        if (!canvas || !window.Chart) return;
        portal.graficos[id]?.destroy();
        const css = getComputedStyle(document.documentElement);
        const cor = nome => css.getPropertyValue(nome).trim() || nome;
        portal.graficos[id] = new Chart(canvas, {
            data: {
                labels: rotulos,
                datasets: series.map(s => ({
                    type: s.tipo,
                    label: s.label,
                    data: s.data,
                    backgroundColor: cor(s.cor),
                    borderColor: cor(s.cor),
                    borderWidth: s.tipo === 'line' ? 2 : 0,
                    borderRadius: 4,
                    tension: 0.3,
                    order: s.tipo === 'line' ? 0 : 1
                }))
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                interaction: { mode: 'index', intersect: false },
                plugins: {
                    legend: { position: 'bottom' },
                    tooltip: {
                        callbacks: {
                            label: c => `${c.dataset.label}: ${c.parsed.y.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })}`
                        }
                    }
                },
                scales: {
                    y: { ticks: { callback: v => v.toLocaleString('pt-BR', { notation: 'compact' }) } }
                }
            }
        });
    }
};
