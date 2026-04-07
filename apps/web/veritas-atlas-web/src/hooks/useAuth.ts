import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { clearAuthState, fetchCurrentUser, login, type LoginRequest } from "../api/auth";

export function useCurrentUser() {
  return useQuery({
    queryKey: ["current-user"],
    queryFn: () => fetchCurrentUser(),
    retry: false,
  });
}

export function useLogin() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (request: LoginRequest) => login(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["current-user"] });
    },
  });
}

export function useLogout() {
  const queryClient = useQueryClient();

  return () => {
    clearAuthState();
    queryClient.removeQueries({ queryKey: ["current-user"] });
  };
}