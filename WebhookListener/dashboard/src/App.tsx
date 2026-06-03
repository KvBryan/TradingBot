import { AuthProvider, useAuth } from './context/AuthContext';
import LoginView from './views/LoginView';
import DashboardView from './views/DashboardView'; // 👈 Importamos la nueva vista completa

function App() {
  return (
    <AuthProvider>
      <MainRouter />
    </AuthProvider>
  );
}

function MainRouter() {
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated) {
    return <LoginView />;
  }

  // Si pasamos la pasarela del login seguro, entramos al centro de control
  return <DashboardView />;
}

export default App;
