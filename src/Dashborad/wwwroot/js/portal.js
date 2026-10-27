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

    // series: [{ label, data, tipo ('bar'|'line'), cor }]; opcoes: { percentual: true } para valores em fração (0,25 = 25%)
    grafico: (id, rotulos, series, opcoes) => {
        const pct = !!opcoes?.percentual;
        const fmt = v => v == null ? '–' : pct
            ? v.toLocaleString('pt-BR', { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 })
            : v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
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
                            label: c => `${c.dataset.label}: ${fmt(c.parsed.y)}`
                        }
                    }
                },
                scales: {
                    y: { ticks: { callback: v => pct ? v.toLocaleString('pt-BR', { style: 'percent' }) : v.toLocaleString('pt-BR', { notation: 'compact' }) } }
                }
            }
        });
    }
};
