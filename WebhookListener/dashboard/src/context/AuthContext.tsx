import React, { createContext, useContext, useState, useEffect, ReactNode } from 'react';

interface AuthContextType {
  token: string | null;
  isAuthenticated: boolean;
  login: (token: string) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Función auxiliar para verificar si el token JWT ha expirado
function isTokenExpired(token: string): boolean {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return true;
    
    // Decodificar Base64Url de forma segura sin librerías externas
    const base64Url = parts[1];
    const base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      window.atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    
    const payload = JSON.parse(jsonPayload);
    if (!payload.exp) return false;
    
    const now = Math.floor(Date.now() / 1000);
    return payload.exp < now;
  } catch (error) {
    return true; // Si el token es inválido o no se puede procesar, se considera expirado
  }
}

export const AuthProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
  const [token, setToken] = useState<string | null>(() => {
    const savedToken = localStorage.getItem('jwt_token');
    if (savedToken && !isTokenExpired(savedToken)) {
      return savedToken;
    }
    localStorage.removeItem('jwt_token');
    return null;
  });

  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(
    () => token !== null && !isTokenExpired(token)
  );

  // Monitorear y limpiar automáticamente tokens expirados periódicamente
  useEffect(() => {
    if (!token) {
      setIsAuthenticated(false);
      return;
    }

    const checkExpiration = () => {
      if (isTokenExpired(token)) {
        logout();
      }
    };

    // Validar inmediatamente
    checkExpiration();

    // Validar cada 15 segundos
    const interval = setInterval(checkExpiration, 15000);
    return () => clearInterval(interval);
  }, [token]);

  const login = (newToken: string) => {
    if (isTokenExpired(newToken)) {
      console.warn('El token proporcionado ya está expirado.');
      return;
    }
    localStorage.setItem('jwt_token', newToken);
    setToken(newToken);
    setIsAuthenticated(true);
    // Redirigir al usuario al dashboard principal
    window.location.href = '/';
  };

  const logout = () => {
    localStorage.removeItem('jwt_token');
    setToken(null);
    setIsAuthenticated(false);
    // Redirigir a la vista de login
    window.location.href = '/login';
  };

  return (
    <AuthContext.Provider value={{ token, isAuthenticated, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth debe ser usado dentro de un AuthProvider');
  }
  return context;
};
