import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Developers portal render error', error, info);
  }

  render() {
    if (!this.state.hasError) return this.props.children;

    return (
      <div style={{ padding: 48, textAlign: 'center', color: '#f87171' }}>
        <h2>Не удалось отобразить раздел</h2>
        <p>Данные раздела повреждены или временно недоступны.</p>
        <button
          onClick={() => window.location.reload()}
          style={{ marginTop: 16, padding: '8px 24px', cursor: 'pointer' }}
        >
          Обновить страницу
        </button>
      </div>
    );
  }
}
